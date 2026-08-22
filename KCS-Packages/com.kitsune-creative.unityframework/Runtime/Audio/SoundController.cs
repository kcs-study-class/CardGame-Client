using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;

namespace UnityFramework.Audio
{
    /// <summary>
    /// BGM/SE 再生を一元管理するシングルトン。
    /// バックエンド (Unity AudioSource / CRI ADX) を <see cref="SoundBackendType"/> で切り替え可能。
    /// 公開 API は文字列 ID または enum を受け取る (生成された <c>ToAddress</c> 拡張と組み合わせて使う)。
    ///
    /// エラー時は例外を投げず、SafeLogger に記録して安全な既定値を返す方針。
    /// </summary>
    [DisallowMultipleComponent]
    public class SoundController : SingletonMonoBehaviour<SoundController>
    {
        private const string PREF_MASTER = "UnityFramework.SoundController.MasterVolume";
        private const string PREF_BGM = "UnityFramework.SoundController.BGMVolume";
        private const string PREF_SE = "UnityFramework.SoundController.SEVolume";
        private const string PREF_MUTE = "UnityFramework.SoundController.IsMuted";

        [Header("Backend")]
        [SerializeField] private SoundBackendType _backendType = SoundBackendType.Unity;

        [Header("Unity Backend Settings")]
        [SerializeField] private AudioMixerGroup _bgmMixerGroup = null;
        [SerializeField] private AudioMixerGroup _seMixerGroup = null;
        [SerializeField, Min(1)] private int _sePoolSize = 8;

        private ISoundBackend _backend = null;

        /// <summary>現在のバックエンド実装。生成は遅延される。</summary>
        public ISoundBackend Backend
        {
            get
            {
                if (_backend == null) _backend = CreateBackend();
                return _backend;
            }
        }

        public SoundBackendType BackendType => _backendType;

        public float MasterVolume { get => Backend.MasterVolume; set => Backend.MasterVolume = value; }
        public float BGMVolume { get => Backend.BGMVolume; set => Backend.BGMVolume = value; }
        public float SEVolume { get => Backend.SEVolume; set => Backend.SEVolume = value; }
        public bool IsMuted { get => Backend.IsMuted; set => Backend.IsMuted = value; }
        public bool IsBGMPlaying => Backend.IsBGMPlaying;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            _backend = CreateBackend();
        }

        protected override void OnDestroy()
        {
            _backend?.Dispose();
            _backend = null;
            base.OnDestroy();
        }

        private ISoundBackend CreateBackend()
        {
            switch (_backendType)
            {
                case SoundBackendType.Unity:
                    return new UnitySoundBackend(transform, _sePoolSize, _bgmMixerGroup, _seMixerGroup);

                case SoundBackendType.CriAdx:
#if UNITY_FRAMEWORK_USE_CRI
                    return new CriAdxSoundBackend(transform, _sePoolSize);
#else
                    SafeLogger.LogError("[SoundController] CRI ADX backend が選択されていますが、UNITY_FRAMEWORK_USE_CRI シンボルが定義されていません。Unity backend にフォールバックします。");
                    return new UnitySoundBackend(transform, _sePoolSize, _bgmMixerGroup, _seMixerGroup);
#endif

                default:
                    return new UnitySoundBackend(transform, _sePoolSize, _bgmMixerGroup, _seMixerGroup);
            }
        }

        // ---- BGM (string id) ----

        /// <summary>
        /// BGM を再生する。<paramref name="fadeTime"/> が 0 より大きい場合はクロスフェード。
        /// </summary>
        public Awaitable PlayBGMAsync(string id, float fadeTime = 0f, CancellationToken cancellationToken = default)
            => Backend.PlayBGMAsync(id, fadeTime, cancellationToken);

        /// <summary>
        /// Intro+Loop の BGM を再生する。<paramref name="introId"/> を 1 回再生したあと <paramref name="loopId"/> に切れ目なく接続する。
        /// Unity backend は dsp 時刻精度で連結。CRI backend は loopId のみ再生 (intro は Cue 内 Loop 領域で表現する想定)。
        /// </summary>
        public Awaitable PlayBGMWithIntroAsync(string introId, string loopId, float fadeTime = 0f, CancellationToken cancellationToken = default)
            => Backend.PlayBGMWithIntroAsync(introId, loopId, fadeTime, cancellationToken);

        public Awaitable FadeOutBGMAsync(float fadeTime = 1f, CancellationToken cancellationToken = default)
            => Backend.FadeOutBGMAsync(fadeTime, cancellationToken);

        public void StopBGM() => Backend.StopBGM();
        public void PauseBGM() => Backend.PauseBGM();
        public void ResumeBGM() => Backend.ResumeBGM();

        // ---- SE (string id) ----

        public void PlaySE(string id, float volumeScale = 1f, float pitch = 1f)
            => Backend.PlaySE(id, volumeScale, pitch);

        public void StopAllSE() => Backend.StopAllSE();

        // ---- Preload / Unload ----

        /// <summary>
        /// サウンドアセットを事前ロードする。SE の初回再生レイテンシ回避に利用。
        /// Unity backend: AudioClip を Addressables からキャッシュへ先読み (参照カウント +1)。
        /// CRI backend: no-op (Cue Sheet 単位の事前ロード前提)。
        /// </summary>
        public Awaitable PreloadAsync(string id, CancellationToken cancellationToken = default)
            => Backend.PreloadAsync(id, cancellationToken);

        /// <summary>
        /// 複数アセットを並列に事前ロードする。
        /// </summary>
        public async Awaitable PreloadAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            if (ids == null) return;
            List<Awaitable> pending = new List<Awaitable>();
            foreach (string id in ids)
            {
                pending.Add(Backend.PreloadAsync(id, cancellationToken));
            }
            foreach (Awaitable task in pending)
            {
                await task;
            }
        }

        /// <summary>enum で事前ロード。</summary>
        public Awaitable PreloadAsync<TEnum>(TEnum id, Func<TEnum, string> addressResolver, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (addressResolver == null)
            {
                SafeLogger.LogError("[SoundController] addressResolver が null です。Preload をスキップしました。");
                return Awaitables.Completed;
            }
            return Backend.PreloadAsync(addressResolver(id), cancellationToken);
        }

        /// <summary>enum 配列で並列事前ロード。</summary>
        public async Awaitable PreloadAsync<TEnum>(IEnumerable<TEnum> ids, Func<TEnum, string> addressResolver, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (ids == null) return;
            if (addressResolver == null)
            {
                SafeLogger.LogError("[SoundController] addressResolver が null です。Preload をスキップしました。");
                return;
            }
            List<string> addresses = new List<string>();
            foreach (TEnum id in ids) addresses.Add(addressResolver(id));
            await PreloadAsync(addresses, cancellationToken);
        }

        /// <summary>事前ロードしたアセットの参照を解放する。</summary>
        public void Unload(string id) => Backend.Unload(id);

        /// <summary>enum 版 Unload。</summary>
        public void Unload<TEnum>(TEnum id, Func<TEnum, string> addressResolver)
            where TEnum : struct, Enum
        {
            if (addressResolver == null)
            {
                SafeLogger.LogError("[SoundController] addressResolver が null です。Unload をスキップしました。");
                return;
            }
            Backend.Unload(addressResolver(id));
        }

        // ---- Enum-based API ----
        // 生成された ~Id enum + ~IdExtensions.ToAddress() と組み合わせて型安全に呼び出す。

        /// <summary>
        /// 生成された <c>ToAddress</c> 拡張メソッドを経由して enum で BGM を再生する。
        /// </summary>
        /// <example>
        /// <code>await SoundController.Instance.PlayBGMAsync(BgmId.Title, addressResolver: id =&gt; id.ToAddress());</code>
        /// </example>
        public Awaitable PlayBGMAsync<TEnum>(TEnum id, Func<TEnum, string> addressResolver, float fadeTime = 0f, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (addressResolver == null)
            {
                SafeLogger.LogError("[SoundController] addressResolver が null です。PlayBGM をスキップしました。");
                return Awaitables.Completed;
            }
            return Backend.PlayBGMAsync(addressResolver(id), fadeTime, cancellationToken);
        }

        /// <summary>
        /// enum で Intro+Loop の BGM を再生する。
        /// </summary>
        /// <example>
        /// <code>await SoundController.Instance.PlayBGMWithIntroAsync(BgmId.SecondDealingIntro, BgmId.SecondDealingLoop, id =&gt; id.ToAddress());</code>
        /// </example>
        public Awaitable PlayBGMWithIntroAsync<TEnum>(TEnum introId, TEnum loopId, Func<TEnum, string> addressResolver, float fadeTime = 0f, CancellationToken cancellationToken = default)
            where TEnum : struct, Enum
        {
            if (addressResolver == null)
            {
                SafeLogger.LogError("[SoundController] addressResolver が null です。PlayBGMWithIntro をスキップしました。");
                return Awaitables.Completed;
            }
            return Backend.PlayBGMWithIntroAsync(addressResolver(introId), addressResolver(loopId), fadeTime, cancellationToken);
        }

        /// <summary>
        /// 生成された <c>ToAddress</c> 拡張メソッドを経由して enum で SE を再生する。
        /// </summary>
        /// <example>
        /// <code>SoundController.Instance.PlaySE(SeId.Click, id =&gt; id.ToAddress());</code>
        /// </example>
        public void PlaySE<TEnum>(TEnum id, Func<TEnum, string> addressResolver, float volumeScale = 1f, float pitch = 1f)
            where TEnum : struct, Enum
        {
            if (addressResolver == null)
            {
                SafeLogger.LogError("[SoundController] addressResolver が null です。PlaySE をスキップしました。");
                return;
            }
            Backend.PlaySE(addressResolver(id), volumeScale, pitch);
        }

        // ---- Preferences ----

        public void SavePreferences()
        {
            PlayerPrefs.SetFloat(PREF_MASTER, Backend.MasterVolume);
            PlayerPrefs.SetFloat(PREF_BGM, Backend.BGMVolume);
            PlayerPrefs.SetFloat(PREF_SE, Backend.SEVolume);
            PlayerPrefs.SetInt(PREF_MUTE, Backend.IsMuted ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void LoadPreferences()
        {
            Backend.MasterVolume = PlayerPrefs.GetFloat(PREF_MASTER, 1f);
            Backend.BGMVolume = PlayerPrefs.GetFloat(PREF_BGM, 1f);
            Backend.SEVolume = PlayerPrefs.GetFloat(PREF_SE, 1f);
            Backend.IsMuted = PlayerPrefs.GetInt(PREF_MUTE, 0) == 1;
        }
    }
}
