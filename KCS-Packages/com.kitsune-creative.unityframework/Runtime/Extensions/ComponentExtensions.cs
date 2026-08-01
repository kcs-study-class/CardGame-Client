using UnityEngine;

namespace UnityFramework.Extensions
{
    public static class ComponentExtensions
    {
        /// <summary>
        /// 同 GameObject から指定コンポーネントを取得し、存在しなければ追加する。
        /// </summary>
        public static T GetOrAddComponent<T>(this Component self) where T : Component
        {
            return self.gameObject.GetOrAddComponent<T>();
        }

        /// <summary>
        /// 親階層方向に指定コンポーネントを検索する。見つからなければ null。
        /// </summary>
        public static T FindInParents<T>(this Component self, bool includeInactive = true) where T : Component
        {
            return self.GetComponentInParent<T>(includeInactive);
        }
    }
}
