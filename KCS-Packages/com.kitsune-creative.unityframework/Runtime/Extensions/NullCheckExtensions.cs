using UnityObject = UnityEngine.Object;

namespace UnityFramework.Extensions
{
    /// <summary>
    /// null チェック用の拡張メソッド。
    /// UnityEngine.Object の "fake null" (Destroy 済みオブジェクト) を正しく判定する。
    /// </summary>
    public static class NullCheckExtensions
    {
        /// <summary>
        /// オブジェクトが null か (Unity Object の場合は破棄済みか) を判定する。
        /// </summary>
        public static bool IsNull<T>(this T self) where T : class
        {
            if (self is UnityObject unityObject)
            {
                return unityObject == null;
            }
            return self is null;
        }

        /// <summary>
        /// オブジェクトが null でない (Unity Object の場合は生存中) かを判定する。
        /// </summary>
        public static bool IsNotNull<T>(this T self) where T : class
        {
            return !self.IsNull();
        }

        /// <summary>
        /// 破棄済みの Unity Object を C# 的な null に変換する。
        /// null 合体演算子 (??) や is null パターンと組み合わせて使う。
        /// </summary>
        /// <example>
        /// <code>GameObject go = mayBeDestroyed.OrNull() ?? fallback;</code>
        /// </example>
        public static T OrNull<T>(this T self) where T : UnityObject
        {
            return self == null ? null : self;
        }
    }
}
