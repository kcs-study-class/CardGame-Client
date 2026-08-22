using UnityEngine;

namespace UnityFramework
{
    /// <summary>
    /// プロジェクト共通の MonoBehaviour 基底クラス。ゲーム側の MonoBehaviour はすべてこれを継承する。
    /// - Transform のキャッシュ
    /// - クラス名プレフィックス付きのログ
    /// - コンポーネント取得の補助
    /// Unity のライフサイクルメソッド (Awake/Start/OnDestroy 等) は横取りしない。
    /// シーンコントローラには <see cref="SceneManagement.SceneBase"/> を使う。
    /// </summary>
    public abstract class MonoBehaviourBase : MonoBehaviour
    {
        private Transform _cachedTransform = null;

        /// <summary>transform のキャッシュ (毎フレーム参照する場合はこちらを使う)。</summary>
        public Transform CachedTransform
        {
            get
            {
                if (_cachedTransform == null)
                {
                    _cachedTransform = transform;
                }
                return _cachedTransform;
            }
        }

        /// <summary>RectTransform として取得 (UI でない場合は null)。</summary>
        public RectTransform CachedRectTransform => CachedTransform as RectTransform;

        /// <summary>コンポーネントを取得し、無ければ追加して返す。</summary>
        protected T GetOrAddComponent<T>() where T : Component
        {
            return TryGetComponent<T>(out T component) ? component : gameObject.AddComponent<T>();
        }

        protected void Log(string message)
        {
            SafeLogger.Log($"[{GetType().Name}] {message}");
        }

        protected void LogWarning(string message)
        {
            SafeLogger.LogWarning($"[{GetType().Name}] {message}");
        }

        protected void LogError(string message)
        {
            SafeLogger.LogError($"[{GetType().Name}] {message}");
        }
    }
}
