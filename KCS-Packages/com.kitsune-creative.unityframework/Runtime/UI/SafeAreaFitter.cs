using UnityEngine;
using UnityEngine.Serialization;

namespace UnityFramework.UI
{
    /// <summary>
    /// RectTransform を SafeArea (ノッチ・ホームインジケータを避けた領域) に合わせて
    /// アンカーで内側に寄せる。Canvas 直下の「全面ストレッチのコンテナ」に付け、
    /// UI要素はその子に置くのが基本形 (背景など全面に敷きたいものはコンテナの外に置く)。
    ///
    /// SafeArea の変化 (回転・リサイズ・デバッグ模擬) は <see cref="ScreenWatcher"/> 経由で追従する。
    /// エッジ単位で無効化できる (例: 下端だけ画面いっぱいまで使う)。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [SerializeField, Tooltip("左端の SafeArea を無視して画面端まで使う"), FormerlySerializedAs("ignoreLeft")] private bool _ignoreLeft = false;
        [SerializeField, Tooltip("右端の SafeArea を無視して画面端まで使う"), FormerlySerializedAs("ignoreRight")] private bool _ignoreRight = false;
        [SerializeField, Tooltip("上端の SafeArea を無視して画面端まで使う"), FormerlySerializedAs("ignoreTop")] private bool _ignoreTop = false;
        [SerializeField, Tooltip("下端の SafeArea を無視して画面端まで使う"), FormerlySerializedAs("ignoreBottom")] private bool _ignoreBottom = false;

        private RectTransform _rect = null;

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
            ScreenWatcher.Instance.SafeAreaChanged += OnSafeAreaChanged;
            Apply(ScreenWatcher.EffectiveSafeArea);
        }

        private void OnDisable()
        {
            if (ScreenWatcher.HasInstance)
            {
                ScreenWatcher.Instance.SafeAreaChanged -= OnSafeAreaChanged;
            }
        }

        private void OnSafeAreaChanged(Rect safeArea)
        {
            Apply(safeArea);
        }

        private void Apply(Rect safeArea)
        {
            float width = Screen.width;
            float height = Screen.height;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            Vector2 anchorMin = new Vector2(
                _ignoreLeft ? 0f : safeArea.xMin / width,
                _ignoreBottom ? 0f : safeArea.yMin / height);
            Vector2 anchorMax = new Vector2(
                _ignoreRight ? 1f : safeArea.xMax / width,
                _ignoreTop ? 1f : safeArea.yMax / height);

            _rect.anchorMin = anchorMin;
            _rect.anchorMax = anchorMax;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
