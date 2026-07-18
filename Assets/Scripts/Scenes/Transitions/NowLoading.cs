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
        private float _dotTimer;
        private int _dotIndex;

        private void Start()
        {
            if (progressFill != null)
            {
                progressFill.fillAmount = 0f;
            }
            SceneController.Instance.SceneLoadProgress += OnSceneLoadProgress;
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
            float deltaTime = Time.unscaledDeltaTime;
            UpdateProgressBar(deltaTime);
            UpdateDots(deltaTime);
            UpdateCard(deltaTime);
        }

        private void UpdateProgressBar(float deltaTime)
        {
            if (progressFill == null)
            {
                return;
            }
            // 実進捗へ一定速度で追従させる。瞬間ジャンプよりも滑らかに見え、
            // 最低表示時間 (minimumDuration) 中の 0.99 張り付きとも相性が良い。
            progressFill.fillAmount = Mathf.MoveTowards(progressFill.fillAmount, _targetProgress, barFollowSpeed * deltaTime);
        }

        private void UpdateDots(float deltaTime)
        {
            if (loadingText == null)
            {
                return;
            }
            _dotTimer += deltaTime;
            if (_dotTimer < dotInterval)
            {
                return;
            }
            _dotTimer -= dotInterval;
            _dotIndex = (_dotIndex + 1) % DotPatterns.Length;
            loadingText.text = DotPatterns[_dotIndex];
        }

        private void UpdateCard(float deltaTime)
        {
            if (loadingCard == null)
            {
                return;
            }
            // Y 軸回転でカードが裏返り続けるフリップ演出。
            loadingCard.Rotate(0f, cardFlipSpeed * deltaTime, 0f);
        }
    }
}
