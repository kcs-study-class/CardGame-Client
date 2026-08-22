using System;
using UnityEngine;

namespace UnityFramework
{
    /// <summary>
    /// 画面サイズ・向き・SafeArea の変化を監視して通知するシングルトン。
    /// ポーリング (毎フレームの値比較) 方式なので、ウィンドウリサイズ (デスクトップ) と
    /// 回転・SafeArea 変化 (モバイル) の両方を同じ仕組みで拾える。
    ///
    /// 使い方:
    /// <code>
    /// ScreenWatcher.Instance.ResolutionChanged += (w, h) => Relayout();
    /// ScreenWatcher.Instance.SafeAreaChanged += rect => ApplySafeArea(rect);
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenWatcher : SingletonMonoBehaviour<ScreenWatcher>
    {
        /// <summary>解像度変化 (width, height)。ウィンドウリサイズ・回転で発火。</summary>
        public event Action<int, int> ResolutionChanged;

        /// <summary>実効 SafeArea の変化 (ピクセル座標、原点は左下)。</summary>
        public event Action<Rect> SafeAreaChanged;

        /// <summary>画面向きの変化 (モバイル)。</summary>
        public event Action<ScreenOrientation> OrientationChanged;

        /// <summary>
        /// デバッグ用: SafeArea を正規化座標 (0..1) で上書きする。
        /// エディタやデスクトップでノッチ端末の SafeArea を模擬するのに使う。null で実機値に戻る。
        /// 例 (横持ち iPhone 相当): <c>new Rect(0.06f, 0.06f, 0.88f, 0.94f)</c>
        /// </summary>
        public static Rect? SimulatedSafeAreaNormalized = null;

        public Vector2Int Resolution { get; private set; }
        public Rect SafeArea { get; private set; }
        public ScreenOrientation Orientation { get; private set; }

        /// <summary>現在の実効 SafeArea (シミュレーション設定を考慮したピクセル矩形)。</summary>
        public static Rect EffectiveSafeArea
        {
            get
            {
                if (SimulatedSafeAreaNormalized is Rect normalized)
                {
                    return new Rect(
                        normalized.x * Screen.width,
                        normalized.y * Screen.height,
                        normalized.width * Screen.width,
                        normalized.height * Screen.height);
                }
                return Screen.safeArea;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            Capture();
        }

        private void Update()
        {
            bool resolutionDirty = Screen.width != Resolution.x || Screen.height != Resolution.y;
            bool safeAreaDirty = EffectiveSafeArea != SafeArea;
            bool orientationDirty = Screen.orientation != Orientation;
            if (!resolutionDirty && !safeAreaDirty && !orientationDirty)
            {
                return;
            }

            Capture();
            if (resolutionDirty)
            {
                ResolutionChanged?.Invoke(Resolution.x, Resolution.y);
            }
            if (orientationDirty)
            {
                OrientationChanged?.Invoke(Orientation);
            }
            if (safeAreaDirty)
            {
                SafeAreaChanged?.Invoke(SafeArea);
            }
        }

        private void Capture()
        {
            Resolution = new Vector2Int(Screen.width, Screen.height);
            SafeArea = EffectiveSafeArea;
            Orientation = Screen.orientation;
        }
    }
}
