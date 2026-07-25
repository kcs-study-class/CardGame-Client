using System;
using Cysharp.Text;
using KTC.Boot;
using KTC.SaveData;
using KTC.UI;
using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;
using UnityFramework.UI;

namespace KTC.Scene
{
    /// <summary>
    /// Title 画面 (仮実装)。任意ボタン入力で TransitionLoading を挟んで Home へ遷移する。
    /// </summary>
    public class Title : MonoBehaviour
    {
        [SerializeField] private CanvasGroup pressPromptGroup;
        [SerializeField] private TMP_Text versionText;

        [Header("演出設定")]
        [SerializeField, Tooltip("待機中の点滅速度")]
        private float blinkSpeed = 2f;

        [SerializeField, Tooltip("決定後の点滅速度")]
        private float confirmedBlinkSpeed = 16f;

        [SerializeField, Tooltip("ローディング画面の最低表示時間 (秒)")]
        private float loadingMinimumDuration = 1.0f;

        private IDisposable _anyButtonListener;
        private MotionHandle _blinkMotion;
        private bool _isTransitioning;
        private bool _bootCompleted;
        private BootPipeline _bootPipeline;
        private BootContext _bootContext;
        private TMP_Text _bootStatusText;

        private async void Start()
        {
            if (versionText != null)
            {
                versionText.text = ZString.Format("v{0}", Application.version);
            }
            if (pressPromptGroup != null)
            {
                pressPromptGroup.alpha = 0f; // ブート完了まで非表示 (完了時に点滅開始)
            }
            CreateBootStatusText();
            // ブート進行表示は日本語なので Noto を適用してから開始
            var noto = await UnityFramework.Resource.ResourceController.Instance
                .LoadAsync<TMP_FontAsset>("Fonts/NotoSansJP", destroyCancellationToken);
            _loadedNotoFont = noto != null;
            if (noto != null && _bootStatusText != null)
            {
                _bootStatusText.font = noto;
            }
            RunBootAsync();
        }

        private bool _loadedNotoFont;

        /// <summary>ブート進行表示 (バージョン表記と同じキャンバスの右下に生成)。</summary>
        private void CreateBootStatusText()
        {
            if (versionText == null)
            {
                return;
            }
            var go = new GameObject("BootStatusText", typeof(RectTransform));
            go.layer = versionText.gameObject.layer;
            go.transform.SetParent(versionText.transform.parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-30f, 20f);
            rt.sizeDelta = new Vector2(1000f, 36f);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = versionText.font;
            text.fontSize = 22f;
            text.color = versionText.color;
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

            var result = await _bootPipeline.RunAsync(_bootContext, destroyCancellationToken);
            if (result.Success)
            {
                _bootCompleted = true;
                SetBootStatus("");
                StartBlink(blinkSpeed);
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
            if (pressPromptGroup == null)
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
                .Bind(pressPromptGroup, static (alpha, group) => group.alpha = alpha)
                .AddTo(gameObject);
        }

        private async void OnAnyButtonPressed()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;
            StartBlink(confirmedBlinkSpeed); // 決定の高速点滅

            // ---- 初回フロー: 利用規約同意 → プレイヤー名入力 ----
            var saveService = SaveDataService.CreateDefault();
            var data = saveService.Load();

            if (!data.IsTermsAccepted)
            {
                TermsModal terms = await ModalController.Instance.OpenAsync<TermsModal>("Modals/Terms");
                if (terms == null)
                {
                    _isTransitioning = false; // ロード失敗時はタイトルに留まる (ログは ModalController 側)
                    return;
                }
                await terms.WaitUntilClosedAsync();
                data.IsTermsAccepted = true;
                saveService.Save(data);
            }

            if (string.IsNullOrEmpty(data.PlayerName))
            {
                var nameModal = await ModalController.Instance.OpenAsync<NameInputModal>("Modals/NameInput");
                if (nameModal == null)
                {
                    _isTransitioning = false;
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
                minimumDuration: loadingMinimumDuration);
        }
    }
}
