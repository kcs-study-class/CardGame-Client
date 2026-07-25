using System;
using System.Collections.Generic;

namespace UnityFramework.Extensions
{
    public static class CollectionExtensions
    {
        /// <summary>
        /// コレクションが null または空かを判定する。
        /// </summary>
        public static bool IsNullOrEmpty<T>(this ICollection<T> self)
        {
            return self == null || self.Count == 0;
        }

        /// <summary>
        /// コレクションが null/空 以外かを判定する。
        /// </summary>
        public static bool IsNotNullOrEmpty<T>(this ICollection<T> self)
        {
            return !self.IsNullOrEmpty();
        }

        /// <summary>
        /// 配列が null または空かを判定する。
        /// </summary>
        public static bool IsNullOrEmpty<T>(this T[] self)
        {
            return self == null || self.Length == 0;
        }

        /// <summary>
        /// 配列が null/空 以外かを判定する。
        /// </summary>
        public static bool IsNotNullOrEmpty<T>(this T[] self)
        {
            return !self.IsNullOrEmpty();
        }

        /// <summary>
        /// ランダムな要素を返す。空の場合は InvalidOperationException。
        /// </summary>
        public static T RandomElement<T>(this IReadOnlyList<T> self)
        {
            if (self == null || self.Count == 0)
            {
                SafeLogger.LogWarning("[CollectionExtensions] 空のコレクションから RandomElement が呼ばれました。default を返します。");
                return default;
            }
            return self[UnityEngine.Random.Range(0, self.Count)];
        }

        /// <summary>
        /// Fisher-Yates シャッフルでリストの中身を入れ替える。
        /// </summary>
        public static void Shuffle<T>(this IList<T> self)
        {
            if (self == null) return;
            for (int i = self.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (self[i], self[j]) = (self[j], self[i]);
            }
        }

        /// <summary>
        /// 各要素に対し action を実行する。self が null の場合は何もしない。
        /// </summary>
        public static void ForEach<T>(this IEnumerable<T> self, Action<T> action)
        {
            if (self == null || action == null) return;
            foreach (var item in self)
            {
                action(item);
            }
        }

        /// <summary>
        /// インデックス付きで各要素に対し action を実行する。
        /// </summary>
        public static void ForEach<T>(this IEnumerable<T> self, Action<T, int> action)
        {
            if (self == null || action == null) return;
            int index = 0;
            foreach (var item in self)
            {
                action(item, index++);
            }
        }
    }
}
