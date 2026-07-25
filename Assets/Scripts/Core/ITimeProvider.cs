using System;

namespace KTC.Core
{
    /// <summary>
    /// 現在時刻のシーム。ゲームコードは DateTime.Now / UtcNow を直接読まず、
    /// 必ずこのインターフェース経由で時刻を取得する。
    /// デバッグメニューの「端末時刻オフセット」やデイリー系機能のテストは、
    /// この実装を差し替えることで実現する。
    /// </summary>
    public interface ITimeProvider
    {
        DateTime UtcNow { get; }
    }

    /// <summary>実時刻をそのまま返す既定実装。</summary>
    public sealed class SystemTimeProvider : ITimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>
    /// オフセット付き時刻 (デバッグ用)。デバッグメニューから Offset を書き換えて
    /// 「明日になったことにする」等を実現する。
    /// </summary>
    public sealed class OffsetTimeProvider : ITimeProvider
    {
        public TimeSpan Offset { get; set; } = TimeSpan.Zero;
        public DateTime UtcNow => DateTime.UtcNow + Offset;
    }
}
