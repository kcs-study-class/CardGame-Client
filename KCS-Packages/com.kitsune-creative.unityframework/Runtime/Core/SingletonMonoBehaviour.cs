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
        private static T s_instance;
        private static readonly object s_lock = new object();
        private static bool s_isQuitting;

        /// <summary>
        /// インスタンスが生成済みかどうか。Instance アクセスによる自動生成を避けたい場合に利用。
        /// </summary>
        public static bool HasInstance => s_instance != null;

        /// <summary>
        /// シングルトンインスタンス。アクセス時に存在しなければ FindAnyObjectByType / 自動生成を行う。
        /// アプリ終了処理中は null を返す。
        /// </summary>
        public static T Instance
        {
            get
            {
                if (s_isQuitting) return null;
                if (s_instance != null) return s_instance;

                lock (s_lock)
                {
                    if (s_instance != null) return s_instance;

                    s_instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
                    if (s_instance != null) return s_instance;

                    if (!Application.isPlaying) return null;

                    var go = new GameObject($"[{typeof(T).Name}]");
                    s_instance = go.AddComponent<T>();
                    DontDestroyOnLoad(go);
                    return s_instance;
                }
            }
        }

        protected virtual void Awake()
        {
            if (s_instance == null)
            {
                s_instance = (T)this;
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (s_instance != this)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnApplicationQuit()
        {
            s_isQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
    }
}
