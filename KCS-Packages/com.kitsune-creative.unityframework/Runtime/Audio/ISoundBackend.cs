using System;
using System.Threading;
using UnityEngine;

namespace UnityFramework.Audio
{
    /// <summary>
    /// サウンド再生のバックエンド抽象。
    /// Unity 標準 (AudioSource) と CRI ADX の実装を差し替え可能にする。
    ///
    /// id の意味はバックエンドにより異なる:
    /// - Unity: Addressables のアドレス (例: "BGM/Title.ogg")
    /// - CRI:   "CueSheetName/CueName" 形式 (例: "MainBgm/Title")
    /// </summary>
    public interface ISoundBackend : IDisposable
    {
        float MasterVolume { get; set; }
        float BGMVolume { get; set; }
        float SEVolume { get; set; }
        bool IsMuted { get; set; }

        bool IsBGMPlaying { get; }

        /// <summary>
        /// BGM を再生する。<paramref name="fadeTime"/> が 0 より大きい場合はクロスフェードを行う。
        /// </summary>
        Awaitable PlayBGMAsync(string id, float fadeTime, CancellationToken cancellationToken);

        /// <summary>
        /// Intro+Loop の BGM を再生する。<paramref name="introId"/> を 1 回再生したのち <paramref name="loopId"/> に切れ目なく接続する。
        /// Unity backend は <c>AudioSource.PlayScheduled</c> による dsp 時刻精度の連結。
        /// CRI ADX backend は本来 Cue 内の Loop 領域で表現する流派のため、警告ログ後 <paramref name="loopId"/> のみ再生にフォールバックする。
        /// </summary>
        Awaitable PlayBGMWithIntroAsync(string introId, string loopId, float fadeTime, CancellationToken cancellationToken);

        /// <summary>BGM をフェードアウトして停止する。</summary>
        Awaitable FadeOutBGMAsync(float fadeTime, CancellationToken cancellationToken);

        /// <summary>BGM を即時停止する。</summary>
        void StopBGM();

        /// <summary>BGM を一時停止する。</summary>
        void PauseBGM();

        /// <summary>一時停止していた BGM を再開する。</summary>
        void ResumeBGM();

        /// <summary>SE をプール内のソースで再生する。</summary>
        void PlaySE(string id, float volumeScale, float pitch);

        /// <summary>再生中の SE を全停止する。</summary>
        void StopAllSE();

        /// <summary>
        /// サウンドアセットを事前ロードする。SE の初回再生レイテンシを抑えたいときに使う。
        /// Unity backend: AudioClip を Addressables 経由でキャッシュに先読み。
        /// CRI backend: 通常は no-op (Cue Sheet ロードは利用側で実施する想定)。
        /// </summary>
        Awaitable PreloadAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// 事前ロードしたアセットの参照を解放する。
        /// Unity backend: ResourceController の参照カウントを-1。
        /// CRI backend: no-op。
        /// </summary>
        void Unload(string id);
    }
}
