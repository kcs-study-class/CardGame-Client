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
        private static T _instance;
        private static readonly object _lock = new object();
        private static bool _isQuitting;

        /// <summary>
        /// インスタンスが生成済みかどうか。Instance アクセスによる自動生成を避けたい場合に利用。
        /// </summary>
        public static bool HasInstance => _instance != null;

        /// <summary>
        /// シングルトンインスタンス。アクセス時に存在しなければ FindAnyObjectByType / 自動生成を行う。
        /// アプリ終了処理中は null を返す。
        /// </summary>
        public static T Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance != null) return _instance;

                lock (_lock)
                {
                    if (_instance != null) return _instance;

                    _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
                    if (_instance != null) return _instance;

                    if (!Application.isPlaying) return null;

                    var go = new GameObject($"[{typeof(T).Name}]");
                    _instance = go.AddComponent<T>();
                    DontDestroyOnLoad(go);
                    return _instance;
                }
            }
        }

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = (T)this;
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
