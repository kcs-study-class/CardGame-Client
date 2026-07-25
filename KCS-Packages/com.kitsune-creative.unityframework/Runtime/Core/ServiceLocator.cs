using System;
using System.Collections.Generic;

namespace UnityFramework
{
    /// <summary>
    /// 型をキーにサービスを登録/解決する簡易ロケーター。
    /// シーン切替時など寿命管理が必要な場面では Clear() / Unregister() を明示的に呼ぶこと。
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> s_services = new Dictionary<Type, object>();
        private static readonly object s_lock = new object();

        /// <summary>
        /// サービスを登録する。既に登録済みの場合は InvalidOperationException。
        /// </summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                SafeLogger.LogError($"[ServiceLocator] service が null です。{typeof(T).Name} の登録をスキップしました。");
                return;
            }
            lock (s_lock)
            {
                var type = typeof(T);
                if (s_services.ContainsKey(type))
                {
                    SafeLogger.LogWarning($"[ServiceLocator] {type.Name} は既に登録されています。RegisterOrReplace を使うか、先に Unregister してください。登録をスキップしました。");
                    return;
                }
                s_services[type] = service;
            }
        }

        /// <summary>
        /// サービスを登録または上書きする。
        /// </summary>
        public static void RegisterOrReplace<T>(T service) where T : class
        {
            if (service == null)
            {
                SafeLogger.LogError($"[ServiceLocator] service が null です。{typeof(T).Name} の登録をスキップしました。");
                return;
            }
            lock (s_lock)
            {
                s_services[typeof(T)] = service;
            }
        }

        /// <summary>
        /// 登録されたサービスを取得する。未登録の場合は InvalidOperationException。
        /// </summary>
        public static T Resolve<T>() where T : class
        {
            lock (s_lock)
            {
                if (s_services.TryGetValue(typeof(T), out var service))
                {
                    return (T)service;
                }
            }
            SafeLogger.LogError($"[ServiceLocator] {typeof(T).Name} は登録されていません。null を返します。");
            return null;
        }

        /// <summary>
        /// 登録されたサービスを取得する。未登録の場合は null を返す。
        /// </summary>
        public static T TryResolve<T>() where T : class
        {
            lock (s_lock)
            {
                return s_services.TryGetValue(typeof(T), out var service) ? (T)service : null;
            }
        }

        /// <summary>
        /// サービスを取得できたら true を返す TryGet パターン。
        /// </summary>
        public static bool TryResolve<T>(out T service) where T : class
        {
            lock (s_lock)
            {
                if (s_services.TryGetValue(typeof(T), out var obj))
                {
                    service = (T)obj;
                    return true;
                }
            }
            service = null;
            return false;
        }

        /// <summary>
        /// 指定型のサービスが登録されているかを判定する。
        /// </summary>
        public static bool IsRegistered<T>() where T : class
        {
            lock (s_lock)
            {
                return s_services.ContainsKey(typeof(T));
            }
        }

        /// <summary>
        /// 指定型のサービス登録を解除する。
        /// </summary>
        public static bool Unregister<T>() where T : class
        {
            lock (s_lock)
            {
                return s_services.Remove(typeof(T));
            }
        }

        /// <summary>
        /// 全てのサービス登録を解除する。シーン切替やテスト終了時に利用する。
        /// </summary>
        public static void Clear()
        {
            lock (s_lock)
            {
                s_services.Clear();
            }
        }
    }
}
