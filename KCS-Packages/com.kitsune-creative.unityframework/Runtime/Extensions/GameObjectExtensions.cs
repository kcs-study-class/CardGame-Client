using UnityEngine;

namespace UnityFramework.Extensions
{
    public static class GameObjectExtensions
    {
        /// <summary>
        /// 指定コンポーネントを取得し、存在しなければ追加する。
        /// </summary>
        public static T GetOrAddComponent<T>(this GameObject self) where T : Component
        {
            return self.TryGetComponent<T>(out var component) ? component : self.AddComponent<T>();
        }

        /// <summary>
        /// 指定コンポーネントを保持しているかを判定する。
        /// </summary>
        public static bool HasComponent<T>(this GameObject self) where T : Component
        {
            return self.TryGetComponent<T>(out _);
        }

        /// <summary>
        /// 子オブジェクトを全て破棄する。
        /// </summary>
        public static void DestroyChildren(this GameObject self)
        {
            self.transform.DestroyChildren();
        }

        /// <summary>
        /// 自身と子孫の Layer を再帰的に設定する。
        /// </summary>
        public static void SetLayerRecursively(this GameObject self, int layer)
        {
            self.layer = layer;
            foreach (Transform child in self.transform)
            {
                child.gameObject.SetLayerRecursively(layer);
            }
        }

        /// <summary>
        /// activeSelf と異なる場合のみ SetActive を呼ぶ。差分が無いときの不要な処理をスキップする。
        /// </summary>
        public static void SetActiveIfNeeded(this GameObject self, bool active)
        {
            if (self.activeSelf != active)
            {
                self.SetActive(active);
            }
        }
    }
}
