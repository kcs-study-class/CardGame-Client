namespace UnityFramework.Extensions
{
    public static class StringExtensions
    {
        /// <summary>
        /// 文字列が null または空かを判定する。
        /// </summary>
        public static bool IsNullOrEmpty(this string self)
        {
            return string.IsNullOrEmpty(self);
        }

        /// <summary>
        /// 文字列が null/空 以外かを判定する。
        /// </summary>
        public static bool IsNotNullOrEmpty(this string self)
        {
            return !string.IsNullOrEmpty(self);
        }

        /// <summary>
        /// 文字列が null または空白文字のみかを判定する。
        /// </summary>
        public static bool IsNullOrWhiteSpace(this string self)
        {
            return string.IsNullOrWhiteSpace(self);
        }

        /// <summary>
        /// 文字列が null/空白以外かを判定する。
        /// </summary>
        public static bool IsNotNullOrWhiteSpace(this string self)
        {
            return !string.IsNullOrWhiteSpace(self);
        }
    }
}
