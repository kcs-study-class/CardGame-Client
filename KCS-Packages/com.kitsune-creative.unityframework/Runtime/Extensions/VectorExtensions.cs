using UnityEngine;

namespace UnityFramework.Extensions
{
    /// <summary>
    /// Vector を不変的に書き換える With 系拡張と相互変換を提供する。
    /// </summary>
    public static class VectorExtensions
    {
        public static Vector3 WithX(this Vector3 self, float x) => new Vector3(x, self.y, self.z);
        public static Vector3 WithY(this Vector3 self, float y) => new Vector3(self.x, y, self.z);
        public static Vector3 WithZ(this Vector3 self, float z) => new Vector3(self.x, self.y, z);

        public static Vector2 WithX(this Vector2 self, float x) => new Vector2(x, self.y);
        public static Vector2 WithY(this Vector2 self, float y) => new Vector2(self.x, y);

        /// <summary>
        /// Vector3 を Vector2 に変換する (z を捨てる)。
        /// </summary>
        public static Vector2 ToVector2(this Vector3 self) => new Vector2(self.x, self.y);

        /// <summary>
        /// Vector2 を Vector3 に変換する (z をオプション指定可能)。
        /// </summary>
        public static Vector3 ToVector3(this Vector2 self, float z = 0f) => new Vector3(self.x, self.y, z);

        /// <summary>
        /// 各成分を符号付きで反転する。
        /// </summary>
        public static Vector3 Inverse(this Vector3 self) => new Vector3(-self.x, -self.y, -self.z);
    }
}
