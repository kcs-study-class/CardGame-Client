using System.Diagnostics;
using UnityObject = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace UnityFramework
{
    /// <summary>
    /// リリースビルドで Log / LogWarning が自動的にストリップされるロガー。
    /// UNITY_EDITOR / DEVELOPMENT_BUILD / ENABLE_LOG のいずれかが定義されている場合のみ出力する。
    /// LogError / LogException は常に有効。
    /// </summary>
    public static class SafeLogger
    {
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("ENABLE_LOG")]
        public static void Log(object message) => Debug.Log(message);

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("ENABLE_LOG")]
        public static void Log(object message, UnityObject context) => Debug.Log(message, context);

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("ENABLE_LOG")]
        public static void LogFormat(string format, params object[] args) => Debug.LogFormat(format, args);

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("ENABLE_LOG")]
        public static void LogWarning(object message) => Debug.LogWarning(message);

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        [Conditional("ENABLE_LOG")]
        public static void LogWarning(object message, UnityObject context) => Debug.LogWarning(message, context);

        public static void LogError(object message) => Debug.LogError(message);
        public static void LogError(object message, UnityObject context) => Debug.LogError(message, context);
        public static void LogException(System.Exception exception) => Debug.LogException(exception);
        public static void LogException(System.Exception exception, UnityObject context) => Debug.LogException(exception, context);
    }
}
