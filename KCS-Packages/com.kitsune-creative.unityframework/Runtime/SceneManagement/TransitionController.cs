using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace UnityFramework.SceneManagement
{
    /// <summary>
    /// 画面トランジション (フェード等) を管理するシングルトン。
    /// 起動時に最前面の Canvas + 全画面 Image + CanvasGroup を自動構築する。
    /// <see cref="SceneController"/> と連携し、シーン遷移時の演出を担う。
    ///
    /// カスタム演出は <see cref="CustomTransition"/> に <see cref="ITransition"/> を設定することで差し替え可能。
    /// </summary>
    [DisallowMultipleComponent]
    public class TransitionController : SingletonMonoBehaviour<TransitionController>
    {
        [Header("Fade Overlay")]
        [SerializeField, FormerlySerializedAs("_fadeColor")] private Color fadeColor = Color.black;
        [SerializeField, FormerlySerializedAs("_sortingOrder")] private int sortingOrder = 32767;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Image _fadeImage;
        private bool _isTransitioning;

        /// <summary>
        /// カスタムトランジション。設定されていれば <see cref="PlayOutAsync"/> / <see cref="PlayInAsync"/> がこちらを使う。
        /// </summary>
        public ITransition CustomTransition { get; set; }

        public bool IsTransitioning => _isTransitioning;

        /// <summary>現在の遮蔽率 (0=透明, 1=完全遮蔽)。</summary>
        public float CurrentAlpha
        {
            get { EnsureSetup(); return _canvasGroup.alpha; }
            set
            {
                EnsureSetup();
                _canvasGroup.alpha = Mathf.Clamp01(value);
                UpdateInputBlocking();
            }
        }

        public Color FadeColor
        {
            get => fadeColor;
            set
            {
                fadeColor = value;
                if (_fadeImage != null) _fadeImage.color = value;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            EnsureSetup();
        }

        private void EnsureSetup()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("[TransitionCanvas]");
            canvasGo.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            var imageGo = new GameObject("FadeImage");
            imageGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var rt = imageGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _fadeImage = imageGo.AddComponent<Image>();
            _fadeImage.color = fadeColor;
            _fadeImage.raycastTarget = true; // フェード時の入力ブロック用
        }

        // ---- Fade API ----

        public async Awaitable FadeOutAsync(float duration = 0.3f, CancellationToken cancellationToken = default)
        {
            EnsureSetup();
            if (_isTransitioning)
            {
                SafeLogger.LogWarning("[TransitionController] 既に遷移中のため、FadeOut リクエストを無視しました。");
                return;
            }
            _isTransitioning = true;
            try
            {
                await FadeInternalAsync(_canvasGroup.alpha, 1f, duration, cancellationToken);
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        public async Awaitable FadeInAsync(float duration = 0.3f, CancellationToken cancellationToken = default)
        {
            EnsureSetup();
            if (_isTransitioning)
            {
                SafeLogger.LogWarning("[TransitionController] 既に遷移中のため、FadeIn リクエストを無視しました。");
                return;
            }
            _isTransitioning = true;
            try
            {
                await FadeInternalAsync(_canvasGroup.alpha, 0f, duration, cancellationToken);
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        public async Awaitable FadeAsync(float from, float to, float duration, CancellationToken cancellationToken = default)
        {
            EnsureSetup();
            await FadeInternalAsync(from, to, duration, cancellationToken);
        }

        private async Awaitable FadeInternalAsync(float from, float to, float duration, CancellationToken cancellationToken)
        {
            _canvasGroup.alpha = from;
            UpdateInputBlocking();

            if (duration <= 0f)
            {
                _canvasGroup.alpha = to;
                UpdateInputBlocking();
                return;
            }

            float t = 0f;
            while (t < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                _canvasGroup.alpha = Mathf.Lerp(from, to, p);
                UpdateInputBlocking();
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            _canvasGroup.alpha = to;
            UpdateInputBlocking();
        }

        // ---- Custom Transition Bridge ----

        /// <summary>
        /// <see cref="CustomTransition"/> が設定されていればそれを実行、なければフェードアウトを実行する。
        /// </summary>
        public Awaitable PlayOutAsync(float fallbackFadeDuration = 0.3f, CancellationToken cancellationToken = default)
        {
            EnsureSetup();
            if (CustomTransition != null)
            {
                return CustomTransition.PlayOutAsync(cancellationToken);
            }
            return FadeOutAsync(fallbackFadeDuration, cancellationToken);
        }

        /// <summary>
        /// <see cref="CustomTransition"/> が設定されていればそれを実行、なければフェードインを実行する。
        /// </summary>
        public Awaitable PlayInAsync(float fallbackFadeDuration = 0.3f, CancellationToken cancellationToken = default)
        {
            EnsureSetup();
            if (CustomTransition != null)
            {
                return CustomTransition.PlayInAsync(cancellationToken);
            }
            return FadeInAsync(fallbackFadeDuration, cancellationToken);
        }

        private void UpdateInputBlocking()
        {
            var blocking = _canvasGroup.alpha > 0.01f;
            _canvasGroup.blocksRaycasts = blocking;
            _canvasGroup.interactable = blocking;
        }

        // ---- Transition Registry ----
        // 複数の ITransition 実装をキーで登録/切替する。CustomTransition の上位 API。

        private readonly Dictionary<string, ITransition> _registry = new Dictionary<string, ITransition>();

        /// <summary>現在登録されているトランジションの読み取り専用ビュー。</summary>
        public IReadOnlyDictionary<string, ITransition> RegisteredTransitions => _registry;

        /// <summary>
        /// 現在アクティブな登録キー。<see cref="CustomTransition"/> を直接設定した場合は null。
        /// 未設定 (フェード使用) の場合も null。
        /// </summary>
        public string ActiveTransitionKey { get; private set; }

        /// <summary>
        /// トランジションをキー付きで登録する。既存キーは上書き。
        /// </summary>
        public void RegisterTransition(string key, ITransition transition)
        {
            if (string.IsNullOrEmpty(key))
            {
                SafeLogger.LogError("[TransitionController] key が空です。登録をスキップしました。");
                return;
            }
            if (transition == null)
            {
                SafeLogger.LogError($"[TransitionController] transition が null です。key='{key}' の登録をスキップしました。");
                return;
            }
            _registry[key] = transition;
        }

        /// <summary>
        /// 登録を解除する。アクティブだった場合は <see cref="CustomTransition"/> を null にしてフェードに戻す。
        /// </summary>
        public bool UnregisterTransition(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (ActiveTransitionKey == key)
            {
                CustomTransition = null;
                ActiveTransitionKey = null;
            }
            return _registry.Remove(key);
        }

        /// <summary>
        /// 登録済みトランジションをアクティブにする。null/空キーでフェード (既定) に戻す。
        /// </summary>
        public void SetActiveTransition(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                CustomTransition = null;
                ActiveTransitionKey = null;
                return;
            }
            if (!_registry.TryGetValue(key, out var transition))
            {
                SafeLogger.LogWarning($"[TransitionController] '{key}' が登録されていません。先に RegisterTransition で登録してください。現在のトランジションを維持します。");
                return;
            }
            CustomTransition = transition;
            ActiveTransitionKey = key;
        }

        /// <summary>
        /// enum でアクティブトランジションを切り替える。
        /// </summary>
        /// <example>
        /// <code>TransitionController.Instance.SetActiveTransition(TransitionType.Curtain, t =&gt; t.ToString());</code>
        /// </example>
        public void SetActiveTransition<TEnum>(TEnum key, Func<TEnum, string> keyResolver)
            where TEnum : struct, Enum
        {
            if (keyResolver == null)
            {
                SafeLogger.LogError("[TransitionController] keyResolver が null です。SetActiveTransition をスキップしました。");
                return;
            }
            SetActiveTransition(keyResolver(key));
        }
    }
}
