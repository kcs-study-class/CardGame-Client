using LitMotion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityFramework.SceneManagement;

namespace KTC.Scene.Transitions
{
    /// <summary>
    /// TransitionLoading シーンのローディング演出。
    /// <see cref="SceneController.SceneLoadProgress"/> を購読してバーとテキストを更新する。
    /// </summary>
    public class NowLoading : MonoBehaviour
    {
        [SerializeField] private Image progressFill;
        [SerializeField] private TMP_Text loadingText;
        [SerializeField] private RectTransform loadingCard;

        [Header("演出設定")]
        [SerializeField, Tooltip("ドットが増える間隔 (秒)")]
        private float dotInterval = 0.4f;

        [SerializeField, Tooltip("カードの回転速度 (度/秒)")]
        private float cardFlipSpeed = 240f;

        [SerializeField, Tooltip("バーが実進捗へ追従する速度 (fillAmount/秒)")]
        private float barFollowSpeed = 1.5f;

        private static readonly string[] DotPatterns = { "Now Loading", "Now Loading.", "Now Loading..", "Now Loading..." };

        private float _targetProgress;

        private void Start()
        {
            if (progressFill != null)
            {
                progressFill.fillAmount = 0f;
            }
            SceneController.Instance.SceneLoadProgress += OnSceneLoadProgress;

            // ドット送りとカードフリップは LitMotion のループに任せる (GameObject 破棄で自動停止)
            if (loadingText != null)
            {
                LMotion.Create(0f, DotPatterns.Length, dotInterval * DotPatterns.Length)
                    .WithLoops(-1, LoopType.Restart)
                    .Bind(loadingText, static (value, text) =>
                        text.text = DotPatterns[Mathf.Min((int)value, DotPatterns.Length - 1)])
                    .AddTo(gameObject);
            }
            if (loadingCard != null)
            {
                LMotion.Create(0f, 360f, 360f / Mathf.Max(1f, cardFlipSpeed))
                    .WithLoops(-1, LoopType.Restart)
                    .Bind(loadingCard, static (angle, card) =>
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
            if (progressFill == null)
            {
                return;
            }
            // 実進捗へ一定速度で追従させる。瞬間ジャンプよりも滑らかに見え、
            // 最低表示時間 (minimumDuration) 中の 0.99 張り付きとも相性が良い。
            // (ターゲットが毎フレーム動く追従なのでトゥイーンではなく MoveTowards のまま)
            progressFill.fillAmount = Mathf.MoveTowards(
                progressFill.fillAmount, _targetProgress, barFollowSpeed * Time.unscaledDeltaTime);
        }
    }
}
