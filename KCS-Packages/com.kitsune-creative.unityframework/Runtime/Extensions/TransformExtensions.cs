using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace UnityFramework.Extensions
{
    public static class TransformExtensions
    {
        /// <summary>
        /// ローカル座標/回転/スケールを初期値にリセットする。
        /// </summary>
        public static void ResetLocal(this Transform self)
        {
            self.localPosition = Vector3.zero;
            self.localRotation = Quaternion.identity;
            self.localScale = Vector3.one;
        }

        /// <summary>
        /// 子オブジェクトを全て破棄する。Play 中は Destroy、Editor では DestroyImmediate を呼ぶ。
        /// </summary>
        public static void DestroyChildren(this Transform self)
        {
            for (int i = self.childCount - 1; i >= 0; i--)
            {
                GameObject child = self.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    UnityObject.Destroy(child);
                }
                else
                {
                    UnityObject.DestroyImmediate(child);
                }
            }
        }

        public static void SetPositionX(this Transform self, float x)
        {
            Vector3 pos = self.position;
            pos.x = x;
            self.position = pos;
        }

        public static void SetPositionY(this Transform self, float y)
        {
            Vector3 pos = self.position;
            pos.y = y;
            self.position = pos;
        }

        public static void SetPositionZ(this Transform self, float z)
        {
            Vector3 pos = self.position;
            pos.z = z;
            self.position = pos;
        }

        public static void SetLocalPositionX(this Transform self, float x)
        {
            Vector3 pos = self.localPosition;
            pos.x = x;
            self.localPosition = pos;
        }

        public static void SetLocalPositionY(this Transform self, float y)
        {
            Vector3 pos = self.localPosition;
            pos.y = y;
            self.localPosition = pos;
        }

        public static void SetLocalPositionZ(this Transform self, float z)
        {
            Vector3 pos = self.localPosition;
            pos.z = z;
            self.localPosition = pos;
        }
    }
}
