using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

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
