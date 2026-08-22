using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;

namespace KTC.Scene.Transitions
{
    /// <summary>
    /// TransitionLoading シーンのローディング演出。
    /// <see cref="SceneController.SceneLoadProgress"/> を購読してバーとテキストを更新する。
    /// </summary>
    public class NowLoading : MonoBehaviourBase
    {
        [SerializeField, FormerlySerializedAs("progressFill")] private Image _progressFill = null;
        [SerializeField, FormerlySerializedAs("loadingText")] private TMP_Text _loadingText = null;
        [SerializeField, FormerlySerializedAs("loadingCard")] private RectTransform _loadingCard = null;

        [Header("演出設定")]
        [SerializeField, Tooltip("ドットが増える間隔 (秒)"), FormerlySerializedAs("dotInterval")]
        private float _dotInterval = 0.4f;

        [SerializeField, Tooltip("カードの回転速度 (度/秒)"), FormerlySerializedAs("cardFlipSpeed")]
        private float _cardFlipSpeed = 240f;

        [SerializeField, Tooltip("バーが実進捗へ追従する速度 (fillAmount/秒)"), FormerlySerializedAs("barFollowSpeed")]
        private float _barFollowSpeed = 1.5f;

        private static readonly string[] DOT_PATTERNS = { "Now Loading", "Now Loading.", "Now Loading..", "Now Loading..." };

        private float _targetProgress = 0f;

        private void Start()
        {
            if (_progressFill != null)
            {
                _progressFill.fillAmount = 0f;
            }
            SceneController.Instance.SceneLoadProgress += OnSceneLoadProgress;

            // ドット送りとカードフリップは LitMotion のループに任せる (GameObject 破棄で自動停止)
            if (_loadingText != null)
            {
                LMotion.Create(0f, DOT_PATTERNS.Length, _dotInterval * DOT_PATTERNS.Length)
                    .WithLoops(-1, LoopType.Restart)
                    .Bind(_loadingText, static (value, text) =>
                        text.text = DOT_PATTERNS[Mathf.Min((int)value, DOT_PATTERNS.Length - 1)])
                    .AddTo(gameObject);
            }
            if (_loadingCard != null)
            {
                LMotion.Create(0f, 360f, 360f / Mathf.Max(1f, _cardFlipSpeed))
                    .WithLoops(-1, LoopType.Restart)
                    .Bind(_loadingCard, static (angle, card) =>
                        card.localRotation = Quaternion.Euler(0f, angle, 0f))
                    .AddTo(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (SceneController.HasInstance)
            {
                SceneController.Instance.SceneLoadProgress -= OnSceneLoadProgress;
            }
        }

        private void OnSceneLoadProgress(string sceneName, float progress)
        {
            _targetProgress = progress;
        }

        private void Update()
        {
            if (_progressFill == null)
            {
                return;
            }
            // 実進捗へ一定速度で追従させる。瞬間ジャンプよりも滑らかに見え、
            // 最低表示時間 (minimumDuration) 中の 0.99 張り付きとも相性が良い。
            // (ターゲットが毎フレーム動く追従なのでトゥイーンではなく MoveTowards のまま)
            _progressFill.fillAmount = Mathf.MoveTowards(
                _progressFill.fillAmount, _targetProgress, _barFollowSpeed * Time.unscaledDeltaTime);
        }
    }
}
