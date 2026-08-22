#if UNITY_FRAMEWORK_USE_CRI
using System;
using System.Threading;
using UnityEngine;
using CriWare;

namespace UnityFramework.Audio
{
    /// <summary>
    /// CRI ADX (criware-unity) を使う SoundBackend 実装。
    /// <c>UNITY_FRAMEWORK_USE_CRI</c> シンボル定義時のみコンパイルされる。
    ///
    /// <para>前提:</para>
    /// <list type="bullet">
    /// <item>CRIWARE Library Initializer / Error Handler がシーンに配置されていること。</item>
    /// <item>使用する Cue Sheet (.acb/.awb) は事前に <c>CriAtom.AddCueSheet</c> で登録済みであること
    /// (Addressables 経由で .acb をロードした後にユーザー側で登録する想定)。</item>
    /// </list>
    ///
    /// <para>id の形式:</para>
    /// <code>"CueSheetName/CueName"</code>
    /// 例: <c>"MainBgm/Title"</c>
    /// </summary>
    public sealed class CriAdxSoundBackend : ISoundBackend
    {
        private const char SHEET_CUE_SEPARATOR = '/';

        private readonly Transform _root;

        private readonly CriAtomSource _bgmPrimary;
        private readonly CriAtomSource _bgmSecondary;
        private readonly CriAtomSource[] _sePool;

        private bool _isPrimaryActive = true;
        private bool _isCrossfading = false;
        private int _seNextIndex = 0;

        private float _masterVolume = 1f;
        private float _bgmVolume = 1f;
        private float _seVolume = 1f;
        private bool _isMuted = false;

        public float MasterVolume { get => _masterVolume; set { _masterVolume = Mathf.Clamp01(value); ApplyBgmVolume(); } }
        public float BGMVolume { get => _bgmVolume; set { _bgmVolume = Mathf.Clamp01(value); ApplyBgmVolume(); } }
        public float SEVolume { get => _seVolume; set { _seVolume = Mathf.Clamp01(value); } }
        public bool IsMuted { get => _isMuted; set { _isMuted = value; ApplyBgmVolume(); } }

        public bool IsBGMPlaying => CurrentBgm().status == CriAtomSource.Status.Playing;

        public CriAdxSoundBackend(Transform parent, int sePoolSize)
        {
            _root = parent;
            _bgmPrimary = CreateSource("BGM_Primary", loop: true);
            _bgmSecondary = CreateSource("BGM_Secondary", loop: true);

            _sePool = new CriAtomSource[Mathf.Max(1, sePoolSize)];
            for (int i = 0; i < _sePool.Length; i++)
            {
                _sePool[i] = CreateSource($"SE_{i:D2}", loop: false);
            }
        }

        private CriAtomSource CreateSource(string objectName, bool loop)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(_root, worldPositionStays: false);
            CriAtomSource src = go.AddComponent<CriAtomSource>();
            src.loop = loop;
            src.playOnStart = false;
            src.volume = 0f;
            return src;
        }

        public async Awaitable PlayBGMAsync(string id, float fadeTime, CancellationToken cancellationToken)
        {
            if (!TrySplit(id, out string sheet, out string cue))
            {
                SafeLogger.LogError($"[CriAdxSoundBackend] id は 'Sheet/Cue' 形式で指定してください: {id}");
                return;
            }

            if (_isCrossfading)
            {
                SafeLogger.LogWarning("[CriAdxSoundBackend] 既にクロスフェード中のため、リクエストを無視しました。");
                return;
            }

            if (fadeTime <= 0f)
            {
                CriAtomSource src = CurrentBgm();
                src.Stop();
                src.cueSheet = sheet;
                src.cueName = cue;
                src.volume = EffectiveBgmVolume();
                src.Play();
                return;
            }

            _isCrossfading = true;
            try
            {
                CriAtomSource fadeOut = CurrentBgm();
                CriAtomSource fadeIn = OtherBgm();

                fadeIn.cueSheet = sheet;
                fadeIn.cueName = cue;
                fadeIn.volume = 0f;
                fadeIn.Play();

                float t = 0f;
                float startOut = fadeOut.volume;
                float targetIn = EffectiveBgmVolume();

                while (t < fadeTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    t += Time.unscaledDeltaTime;
                    float p = Mathf.Clamp01(t / fadeTime);
                    fadeOut.volume = Mathf.Lerp(startOut, 0f, p);
                    fadeIn.volume = Mathf.Lerp(0f, targetIn, p);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                fadeOut.Stop();
                _isPrimaryActive = !_isPrimaryActive;
            }
            finally
            {
                _isCrossfading = false;
            }
        }

        public Awaitable PlayBGMWithIntroAsync(string introId, string loopId, float fadeTime, CancellationToken cancellationToken)
        {
            // CRI ADX では intro+loop は単一 Cue 内の Loop 領域 (Loop Start / Loop End マーカー) で表現するのが推奨。
            // ここでは loopId のみを通常再生にフォールバックする。
            SafeLogger.LogWarning($"[CriAdxSoundBackend] CRI ADX では intro+loop は単一 Cue の Loop 領域で表現してください。introId='{introId}' を無視して loopId='{loopId}' のみ再生します。");
            return PlayBGMAsync(loopId, fadeTime, cancellationToken);
        }

        public async Awaitable FadeOutBGMAsync(float fadeTime, CancellationToken cancellationToken)
        {
            CriAtomSource src = CurrentBgm();
            if (src.status != CriAtomSource.Status.Playing) return;

            float start = src.volume;
            float t = 0f;
            while (t < fadeTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                t += Time.unscaledDeltaTime;
                float p = fadeTime <= 0f ? 1f : Mathf.Clamp01(t / fadeTime);
                src.volume = Mathf.Lerp(start, 0f, p);
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            src.Stop();
        }

        public void StopBGM()
        {
            _bgmPrimary.Stop();
            _bgmSecondary.Stop();
        }

        public void PauseBGM()
        {
            _bgmPrimary.Pause(true);
            _bgmSecondary.Pause(true);
        }

        public void ResumeBGM()
        {
            _bgmPrimary.Pause(false);
            _bgmSecondary.Pause(false);
        }

        public void PlaySE(string id, float volumeScale, float pitch)
        {
            if (!TrySplit(id, out string sheet, out string cue))
            {
                SafeLogger.LogError($"[CriAdxSoundBackend] id は 'Sheet/Cue' 形式で指定してください: {id}");
                return;
            }

            CriAtomSource source = _sePool[_seNextIndex];
            _seNextIndex = (_seNextIndex + 1) % _sePool.Length;

            source.Stop();
            source.cueSheet = sheet;
            source.cueName = cue;
            source.volume = Mathf.Clamp01(volumeScale) * EffectiveSeVolume();
            // CRI の pitch は cent 単位 (1200 cent = 1 octave)。pitch=1.0 を 0 cent として変換。
            source.pitch = (pitch - 1f) * 1200f;
            source.Play();
        }

        public void StopAllSE()
        {
            foreach (CriAtomSource s in _sePool) s.Stop();
        }

        public Awaitable PreloadAsync(string id, CancellationToken cancellationToken)
        {
            // CRI ADX は Cue Sheet (.acb) 単位でロードする設計のため、個別 Cue の preload は不要。
            // Cue Sheet 自体のロード/解放は利用側で `CriAtom.AddCueSheet` / `RemoveCueSheet` を呼び出す想定。
            AwaitableCompletionSource source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        public void Unload(string id)
        {
            // 同上、CRI では個別 Cue の unload は行わない。
        }

        public void Dispose()
        {
            StopBGM();
            StopAllSE();
        }

        private static bool TrySplit(string id, out string sheet, out string cue)
        {
            sheet = null;
            cue = null;
            if (string.IsNullOrEmpty(id)) return false;
            int idx = id.IndexOf(SHEET_CUE_SEPARATOR);
            if (idx <= 0 || idx >= id.Length - 1) return false;
            sheet = id.Substring(0, idx);
            cue = id.Substring(idx + 1);
            return true;
        }

        private CriAtomSource CurrentBgm() => _isPrimaryActive ? _bgmPrimary : _bgmSecondary;
        private CriAtomSource OtherBgm() => _isPrimaryActive ? _bgmSecondary : _bgmPrimary;
        private float EffectiveBgmVolume() => _isMuted ? 0f : _masterVolume * _bgmVolume;
        private float EffectiveSeVolume() => _isMuted ? 0f : _masterVolume * _seVolume;

        private void ApplyBgmVolume()
        {
            if (_isCrossfading) return;
            if (_bgmPrimary != null && _bgmPrimary.status == CriAtomSource.Status.Playing)
                _bgmPrimary.volume = EffectiveBgmVolume();
            if (_bgmSecondary != null && _bgmSecondary.status == CriAtomSource.Status.Playing)
                _bgmSecondary.volume = EffectiveBgmVolume();
        }
    }
}
#endif
