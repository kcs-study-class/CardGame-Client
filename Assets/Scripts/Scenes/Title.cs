using System;
using Cysharp.Text;
using KTC.Boot;
using UnityFramework.Boot;
using KTC.SaveData;
using KTC.UI;
using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;
using UnityFramework.UI;

namespace KTC.Scene
{
    /// <summary>
    /// Title 画面 (仮実装)。任意ボタン入力で TransitionLoading を挟んで Home へ遷移する。
    /// </summary>
    public class Title : SceneBase
    {
        [SerializeField, FormerlySerializedAs("pressPromptGroup")] private CanvasGroup _pressPromptGroup = null;
        [SerializeField, FormerlySerializedAs("versionText")] private TMP_Text _versionText = null;

        [Header("演出設定")]
        [SerializeField, Tooltip("待機中の点滅速度"), FormerlySerializedAs("blinkSpeed")]
        private float _blinkSpeed = 2f;

        [SerializeField, Tooltip("決定後の点滅速度"), FormerlySerializedAs("confirmedBlinkSpeed")]
        private float _confirmedBlinkSpeed = 16f;

        [SerializeField, Tooltip("ローディング画面の最低表示時間 (秒)"), FormerlySerializedAs("loadingMinimumDuration")]
        private float _loadingMinimumDuration = 1.0f;

        [SerializeField] private Button _plessWindowButton = default;

        private IDisposable _anyButtonListener = null;
        private MotionHandle _blinkMotion = default;
        private bool _bootCompleted = false;
        private BootPipeline _bootPipeline = null;
        private BootContext _bootContext = null;
        private TMP_Text _bootStatusText = null;

        protected override Awaitable OnPrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            return Awaitables.Completed; // タイトルの準備は OnStartAsync のブート処理で行う
        }

        protected override async Awaitable OnStartAsync()
        {
            if (_versionText != null)
            {
                _versionText.text = ZString.Format("v{0}", Application.version);
            }
            if (_pressPromptGroup != null)
            {
                _pressPromptGroup.alpha = 0f; // ブート完了まで非表示 (完了時に点滅開始)
            }
            CreateBootStatusText();
            // ブート進行表示は日本語なので Noto を適用してから開始
            TMP_FontAsset noto = await UnityFramework.Resource.ResourceController.Instance
                .LoadAsync<TMP_FontAsset>("Fonts/NotoSansJP", destroyCancellationToken);
            _loadedNotoFont = noto != null;
            if (noto != null && _bootStatusText != null)
            {
                _bootStatusText.font = noto;
            }
            GameAudio.PlayBgm(GameAudio.MENU_BGM);
            RunBootAsync();
        }

        private bool _loadedNotoFont = false;

        /// <summary>ブート進行表示 (バージョン表記と同じキャンバスの右下に生成)。</summary>
        private void CreateBootStatusText()
        {
            if (_versionText == null)
            {
                return;
            }
            GameObject go = new GameObject("BootStatusText", typeof(RectTransform));
            go.layer = _versionText.gameObject.layer;
            go.transform.SetParent(_versionText.transform.parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-30f, 20f);
            rt.sizeDelta = new Vector2(1000f, 36f);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = _versionText.font;
            text.fontSize = 22f;
            text.color = _versionText.color;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.raycastTarget = false;
            _bootStatusText = text;
        }

        /// <summary>
        /// タイトル表示の裏で走るブート処理。完了するまでスタート入力を受け付けない。
        /// 失敗時は任意キーで「失敗したタスクから」リトライする。
        /// </summary>
        private async void RunBootAsync()
        {
            if (_bootPipeline == null)
            {
                _bootContext = new BootContext();
                _bootPipeline = new BootPipeline(new IBootTask[]
                {
                    new LoadSaveDataTask(),
                    new MaintenanceCheckTask(),
                    new LoginTask(),
                    new FetchNoticesTask(),
                    new AssetUpdateCheckTask(),
                });
                _bootPipeline.TaskStarted += (index, total, name) =>
                    SetBootStatus(ZString.Format("{0}... ({1}/{2})", name, index + 1, total));
            }

            BootResult result = await _bootPipeline.RunAsync(_bootContext, destroyCancellationToken);
            if (result.Success)
            {
                _bootCompleted = true;
                SetBootStatus("");
                StartBlink(_blinkSpeed);
                _anyButtonListener = InputSystem.onAnyButtonPress.CallOnce(_ => OnAnyButtonPressed());
            }
            else
            {
                SetBootStatus(ZString.Format("{0}に失敗しました: {1}  - 任意キーでリトライ -", result.FailedTaskName, result.Message));
                _anyButtonListener = InputSystem.onAnyButtonPress.CallOnce(_ =>
                {
                    _anyButtonListener?.Dispose();
                    RunBootAsync();
                });
            }
        }

        private void SetBootStatus(string message)
        {
            if (_bootStatusText != null)
            {
                _bootStatusText.text = message;
            }
        }

        private void OnDestroy()
        {
            _anyButtonListener?.Dispose();
            if (_loadedNotoFont && UnityFramework.Resource.ResourceController.HasInstance)
            {
                UnityFramework.Resource.ResourceController.Instance.Release("Fonts/NotoSansJP");
            }
        }

        /// <summary>プロンプト点滅 (LitMotion)。speed は従来の sin 角速度と互換の指定。</summary>
        private void StartBlink(float speed)
        {
            if (_pressPromptGroup == null)
            {
                return;
            }
            if (_blinkMotion.IsActive())
            {
                _blinkMotion.Cancel();
            }
            float halfPeriod = Mathf.PI / Mathf.Max(0.01f, speed);
            _blinkMotion = LMotion.Create(0.15f, 1f, halfPeriod)
                .WithLoops(-1, LoopType.Yoyo)
                .WithEase(Ease.InOutSine)
                .Bind(_pressPromptGroup, static (alpha, group) => group.alpha = alpha)
                .AddTo(gameObject);
        }

        private async void OnAnyButtonPressed()
        {
            if (!TryBeginTransition())
            {
                return;
            }
            StartBlink(_confirmedBlinkSpeed); // 決定の高速点滅

            // ---- 初回フロー: 利用規約同意 → プレイヤー名入力 ----
            SaveDataService saveService = SaveDataService.CreateDefault();
            PlayerData data = saveService.Load();

            if (!data.IsTermsAccepted)
            {
                TermsModal terms = await ModalController.Instance.OpenAsync<TermsModal>("Modals/Terms");
                if (terms == null)
                {
                    CancelTransition(); // ロード失敗時はタイトルに留まる (ログは ModalController 側)
                    return;
                }
                await terms.WaitUntilClosedAsync();
                data.IsTermsAccepted = true;
                saveService.Save(data);
            }

            if (string.IsNullOrEmpty(data.PlayerName))
            {
                NameInputModal nameModal = await ModalController.Instance.OpenAsync<NameInputModal>("Modals/NameInput");
                if (nameModal == null)
                {
                    CancelTransition();
                    return;
                }
                await nameModal.WaitUntilClosedAsync();
                data.PlayerName = nameModal.ResultName;
                saveService.Save(data);
            }

            // 注意: destroyCancellationToken は渡さない。遷移の途中で Title シーン自身が
            // アンロードされるため、渡すとロード処理が中途キャンセルされてしまう。
            await SceneController.Instance.LoadSceneViaTransitionSceneAsync(
                SceneId.Home,
                SceneId.TransitionLoading,
                SceneIdExtensions.ToSceneName,
                minimumDuration: _loadingMinimumDuration);
        }
    }
}
