using System;
using KTC.SaveData;
using KTC.UI;
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
        private float _blinkPhase;
        private bool _isTransitioning;

        private void Start()
        {
            if (versionText != null)
            {
                versionText.text = "v" + Application.version;
            }
            _anyButtonListener = InputSystem.onAnyButtonPress.CallOnce(_ => OnAnyButtonPressed());
        }

        private void OnDestroy()
        {
            _anyButtonListener?.Dispose();
        }

        private void Update()
        {
            if (pressPromptGroup == null)
            {
                return;
            }
            // 位相を積算して速度変更時も連続的に点滅させる。完全消灯は避ける。
            _blinkPhase += (_isTransitioning ? confirmedBlinkSpeed : blinkSpeed) * Time.unscaledDeltaTime;
            float wave = 0.5f + 0.5f * Mathf.Sin(_blinkPhase);
            pressPromptGroup.alpha = Mathf.Lerp(0.15f, 1f, wave);
        }

        private async void OnAnyButtonPressed()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;

            // ---- 初回フロー: 利用規約同意 → プレイヤー名入力 ----
            var saveService = SaveDataService.CreateDefault();
            var data = saveService.Load();

            if (!data.IsTermsAccepted)
            {
                var terms = await ModalController.Instance.OpenAsync<TermsModal>("Modals/Terms");
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
