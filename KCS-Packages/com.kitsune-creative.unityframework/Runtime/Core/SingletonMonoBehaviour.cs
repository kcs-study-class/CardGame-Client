using UnityEngine;

namespace UnityFramework
{
    /// <summary>
    /// シングルトン MonoBehaviour の基底クラス。
    /// シーンに存在しなければ自動生成され、ルート階層の場合は DontDestroyOnLoad で永続化される。
    /// </summary>
    /// <typeparam name="T">継承する具体型 (CRTP)</typeparam>
    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : SingletonMonoBehaviour<T>
    {
        private static T CachedInstance;
        private static readonly object Lock = new object();
        private static bool IsQuitting;

        /// <summary>
        /// インスタンスが生成済みかどうか。Instance アクセスによる自動生成を避けたい場合に利用。
        /// </summary>
        public static bool HasInstance => CachedInstance != null;

        /// <summary>
        /// シングルトンインスタンス。アクセス時に存在しなければ FindAnyObjectByType / 自動生成を行う。
        /// アプリ終了処理中は null を返す。
        /// </summary>
        public static T Instance
        {
            get
            {
                if (IsQuitting) return null;
                if (CachedInstance != null) return CachedInstance;

                lock (Lock)
                {
                    if (CachedInstance != null) return CachedInstance;

                    CachedInstance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
                    if (CachedInstance != null) return CachedInstance;

                    if (!Application.isPlaying) return null;

                    var go = new GameObject($"[{typeof(T).Name}]");
                    CachedInstance = go.AddComponent<T>();
                    DontDestroyOnLoad(go);
                    return CachedInstance;
                }
            }
        }

        protected virtual void Awake()
        {
            if (CachedInstance == null)
            {
                CachedInstance = (T)this;
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (CachedInstance != this)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnApplicationQuit()
        {
            IsQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            if (CachedInstance == this)
            {
                CachedInstance = null;
            }
        }
    }
}
