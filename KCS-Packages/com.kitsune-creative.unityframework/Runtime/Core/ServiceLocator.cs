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
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();
        private static readonly object Lock = new object();

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
            lock (Lock)
            {
                var type = typeof(T);
                if (Services.ContainsKey(type))
                {
                    SafeLogger.LogWarning($"[ServiceLocator] {type.Name} は既に登録されています。RegisterOrReplace を使うか、先に Unregister してください。登録をスキップしました。");
                    return;
                }
                Services[type] = service;
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
            lock (Lock)
            {
                Services[typeof(T)] = service;
            }
        }

        /// <summary>
        /// 登録されたサービスを取得する。未登録の場合は InvalidOperationException。
        /// </summary>
        public static T Resolve<T>() where T : class
        {
            lock (Lock)
            {
                if (Services.TryGetValue(typeof(T), out var service))
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
            lock (Lock)
            {
                return Services.TryGetValue(typeof(T), out var service) ? (T)service : null;
            }
        }

        /// <summary>
        /// サービスを取得できたら true を返す TryGet パターン。
        /// </summary>
        public static bool TryResolve<T>(out T service) where T : class
        {
            lock (Lock)
            {
                if (Services.TryGetValue(typeof(T), out var obj))
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
            lock (Lock)
            {
                return Services.ContainsKey(typeof(T));
            }
        }

        /// <summary>
        /// 指定型のサービス登録を解除する。
        /// </summary>
        public static bool Unregister<T>() where T : class
        {
            lock (Lock)
            {
                return Services.Remove(typeof(T));
            }
        }

        /// <summary>
        /// 全てのサービス登録を解除する。シーン切替やテスト終了時に利用する。
        /// </summary>
        public static void Clear()
        {
            lock (Lock)
            {
                Services.Clear();
            }
        }
    }
}
