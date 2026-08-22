using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using UnityFramework.Extensions;
using UnityFramework.Resource;

namespace UnityFramework.Audio
{
    /// <summary>
    /// Unity AudioSource + Addressables(ResourceController) を使う SoundBackend 実装。
    /// id は Addressables のアドレス文字列を期待する。
    ///
    /// 内部に 3 本の AudioSource を持つ:
    /// - <c>_bgmPrimary</c> / <c>_bgmSecondary</c>: クロスフェード用のスワップペア
    /// - <c>_bgmIntro</c>: Intro+Loop 再生時の intro 専用ソース
    /// </summary>
    public sealed class UnitySoundBackend : ISoundBackend
    {
        private readonly Transform _root;

        private readonly AudioSource _bgmPrimary;
        private readonly AudioSource _bgmSecondary;
        private readonly AudioSource _bgmIntro;
        private readonly AudioSource[] _sePool;

        private bool _isPrimaryActive = true;
        private bool _isCrossfading = false;
        private string _currentBgmAddress = null;
        private string _currentBgmIntroAddress = null;
        private int _seNextIndex = 0;

        private float _masterVolume = 1f;
        private float _bgmVolume = 1f;
        private float _seVolume = 1f;
        private bool _isMuted = false;

        public float MasterVolume { get => _masterVolume; set { _masterVolume = Mathf.Clamp01(value); ApplyBgmVolume(); } }
        public float BGMVolume { get => _bgmVolume; set { _bgmVolume = Mathf.Clamp01(value); ApplyBgmVolume(); } }
        public float SEVolume { get => _seVolume; set { _seVolume = Mathf.Clamp01(value); } }
        public bool IsMuted { get => _isMuted; set { _isMuted = value; ApplyBgmVolume(); } }

        public bool IsBGMPlaying => CurrentBgm().isPlaying || _bgmIntro.isPlaying;

        public UnitySoundBackend(Transform parent, int sePoolSize, AudioMixerGroup bgmGroup, AudioMixerGroup seGroup)
        {
            _root = parent;
            _bgmPrimary = CreateAudioSource("BGM_Primary", bgmGroup, loop: true);
            _bgmSecondary = CreateAudioSource("BGM_Secondary", bgmGroup, loop: true);
            _bgmIntro = CreateAudioSource("BGM_Intro", bgmGroup, loop: false);

            _sePool = new AudioSource[Mathf.Max(1, sePoolSize)];
            for (int i = 0; i < _sePool.Length; i++)
            {
                _sePool[i] = CreateAudioSource($"SE_{i:D2}", seGroup, loop: false);
            }
        }

        private AudioSource CreateAudioSource(string objectName, AudioMixerGroup group, bool loop)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(_root, worldPositionStays: false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.outputAudioMixerGroup = group;
            source.volume = 0f;
            return source;
        }

        public async Awaitable PlayBGMAsync(string id, float fadeTime, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                SafeLogger.LogWarning("[UnitySoundBackend] BGM id が空です。");
                return;
            }

            if (_isCrossfading)
            {
                SafeLogger.LogWarning("[UnitySoundBackend] 既にクロスフェード中のため、リクエストを無視しました。");
                return;
            }

            AudioClip clip;
            try
            {
                clip = await ResourceController.Instance.LoadAsync<AudioClip>(id, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SafeLogger.LogError($"[UnitySoundBackend] BGM ロード失敗: {id} - {ex.Message}");
                return;
            }

            if (clip.IsNull()) return;

            if (fadeTime <= 0f)
            {
                AudioSource src = CurrentBgm();
                src.clip = clip;
                src.volume = EffectiveBgmVolume();
                src.Play();
                ReleasePreviousBgm(id);
                _currentBgmAddress = id;
                return;
            }

            _isCrossfading = true;
            try
            {
                AudioSource fadeOut = CurrentBgm();
                AudioSource fadeIn = OtherBgm();

                fadeIn.clip = clip;
                fadeIn.volume = 0f;
                fadeIn.Play();

                float t = 0f;
                float startOut = fadeOut.volume;
                float startIntroVol = _bgmIntro.volume;
                bool introWasPlaying = _bgmIntro.isPlaying;
                float targetIn = EffectiveBgmVolume();

                while (t < fadeTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    t += Time.unscaledDeltaTime;
                    float p = Mathf.Clamp01(t / fadeTime);
                    fadeOut.volume = Mathf.Lerp(startOut, 0f, p);
                    if (introWasPlaying && _bgmIntro.isPlaying)
                    {
                        _bgmIntro.volume = Mathf.Lerp(startIntroVol, 0f, p);
                    }
                    fadeIn.volume = Mathf.Lerp(0f, targetIn, p);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }

                fadeOut.Stop();
                fadeOut.clip = null;
                ReleasePreviousBgm(id);
                _currentBgmAddress = id;
                _isPrimaryActive = !_isPrimaryActive;
            }
            finally
            {
                _isCrossfading = false;
            }
        }

        /// <summary>
        /// Intro+Loop 連結再生。intro を 1 回再生したのち loop に切れ目なく接続する。
        /// <c>AudioSource.PlayScheduled</c> による dsp 時刻 (sample 精度) で連結。
        /// intro と loop のサンプルレート (frequency) は揃えること。
        /// </summary>
        public async Awaitable PlayBGMWithIntroAsync(string introId, string loopId, float fadeTime, CancellationToken cancellationToken)
        {
            // intro が空ならただの BGM 再生に縮退
            if (string.IsNullOrEmpty(introId))
            {
                await PlayBGMAsync(loopId, fadeTime, cancellationToken);
                return;
            }
            if (string.IsNullOrEmpty(loopId))
            {
                SafeLogger.LogWarning("[UnitySoundBackend] loopId が空です。");
                return;
            }

            if (_isCrossfading)
            {
                SafeLogger.LogWarning("[UnitySoundBackend] 既にクロスフェード中のため、リクエストを無視しました。");
                return;
            }

            AudioClip introClip, loopClip;
            try
            {
                introClip = await ResourceController.Instance.LoadAsync<AudioClip>(introId, cancellationToken);
                loopClip = await ResourceController.Instance.LoadAsync<AudioClip>(loopId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SafeLogger.LogError($"[UnitySoundBackend] Intro+Loop ロード失敗: intro={introId} loop={loopId} - {ex.Message}");
                return;
            }

            if (introClip.IsNull() || loopClip.IsNull()) return;

            if (introClip.frequency != loopClip.frequency)
            {
                SafeLogger.LogWarning($"[UnitySoundBackend] intro と loop のサンプルレートが異なります (intro={introClip.frequency}Hz, loop={loopClip.frequency}Hz)。継ぎ目に違和感が出る可能性があります。");
            }

            _isCrossfading = true;
            try
            {
                // 旧 intro を停止 (新 intro を貼り直すため)
                StopIntroIfActive();

                AudioSource oldSource = CurrentBgm();
                AudioSource loopSource = OtherBgm();

                bool needFade = fadeTime > 0f && oldSource.isPlaying;
                float targetVolume = EffectiveBgmVolume();
                float startVolume = needFade ? 0f : targetVolume;

                loopSource.clip = loopClip;
                loopSource.loop = true;
                loopSource.volume = startVolume;

                _bgmIntro.clip = introClip;
                _bgmIntro.loop = false;
                _bgmIntro.volume = startVolume;

                // dsp 時刻 (サンプル精度) で intro→loop を継ぎ目なく連結
                double startTime = AudioSettings.dspTime + 0.1; // 100ms の準備マージン
                double loopStartTime = startTime + (double)introClip.samples / introClip.frequency;

                _bgmIntro.PlayScheduled(startTime);
                loopSource.PlayScheduled(loopStartTime);

                if (needFade)
                {
                    float t = 0f;
                    float oldStart = oldSource.volume;

                    while (t < fadeTime)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        t += Time.unscaledDeltaTime;
                        float p = Mathf.Clamp01(t / fadeTime);
                        oldSource.volume = Mathf.Lerp(oldStart, 0f, p);
                        float newVol = Mathf.Lerp(0f, targetVolume, p);
                        _bgmIntro.volume = newVol;
                        loopSource.volume = newVol;
                        await Awaitable.NextFrameAsync(cancellationToken);
                    }
                }

                oldSource.Stop();
                oldSource.clip = null;

                // 旧 loop 参照を解放 (新と同じなら据え置き)
                if (!string.IsNullOrEmpty(_currentBgmAddress) && _currentBgmAddress != loopId)
                {
                    ResourceController.Instance.Release(_currentBgmAddress);
                }

                _currentBgmIntroAddress = introId;
                _currentBgmAddress = loopId;
                _isPrimaryActive = !_isPrimaryActive;
            }
            finally
            {
                _isCrossfading = false;
            }
        }

        public async Awaitable FadeOutBGMAsync(float fadeTime, CancellationToken cancellationToken)
        {
            AudioSource src = CurrentBgm();
            if (!src.isPlaying && !_bgmIntro.isPlaying) return;

            float t = 0f;
            float startMain = src.volume;
            float startIntro = _bgmIntro.volume;
            while (t < fadeTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                t += Time.unscaledDeltaTime;
                float p = fadeTime <= 0f ? 1f : Mathf.Clamp01(t / fadeTime);
                src.volume = Mathf.Lerp(startMain, 0f, p);
                if (_bgmIntro.isPlaying) _bgmIntro.volume = Mathf.Lerp(startIntro, 0f, p);
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            src.Stop();
            ReleasePreviousBgm(null);
        }

        public void StopBGM()
        {
            _bgmPrimary.Stop();
            _bgmSecondary.Stop();
            ReleasePreviousBgm(null);
        }

        public void PauseBGM()
        {
            _bgmPrimary.Pause();
            _bgmSecondary.Pause();
            _bgmIntro.Pause();
        }

        public void ResumeBGM()
        {
            _bgmPrimary.UnPause();
            _bgmSecondary.UnPause();
            _bgmIntro.UnPause();
        }

        public void PlaySE(string id, float volumeScale, float pitch)
        {
            if (string.IsNullOrEmpty(id)) return;

            AudioClip cached = ResourceController.Instance.Get<AudioClip>(id);
            if (cached.IsNotNull())
            {
                PlaySEInternal(cached, volumeScale, pitch);
                return;
            }
            _ = LoadAndPlaySEAsync(id, volumeScale, pitch);
        }

        private async Awaitable LoadAndPlaySEAsync(string id, float volumeScale, float pitch)
        {
            try
            {
                AudioClip clip = await ResourceController.Instance.LoadAsync<AudioClip>(id);
                if (clip.IsNotNull())
                {
                    PlaySEInternal(clip, volumeScale, pitch);
                }
            }
            catch (Exception ex)
            {
                SafeLogger.LogError($"[UnitySoundBackend] SE ロード失敗: {id} - {ex.Message}");
            }
        }

        private void PlaySEInternal(AudioClip clip, float volumeScale, float pitch)
        {
            AudioSource source = _sePool[_seNextIndex];
            _seNextIndex = (_seNextIndex + 1) % _sePool.Length;

            source.clip = clip;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(volumeScale) * EffectiveSeVolume();
            source.Play();
        }

        public void StopAllSE()
        {
            foreach (AudioSource s in _sePool) s.Stop();
        }

        public async Awaitable PreloadAsync(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id)) return;
            try
            {
                await ResourceController.Instance.PreloadAsync<AudioClip>(id, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SafeLogger.LogError($"[UnitySoundBackend] Preload 失敗: {id} - {ex.Message}");
            }
        }

        public void Unload(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            ResourceController.Instance.Release(id);
        }

        public void Dispose()
        {
            StopBGM();
            StopAllSE();
            // GameObjects は親 (SoundController) と一緒に破棄される想定
        }

        private AudioSource CurrentBgm() => _isPrimaryActive ? _bgmPrimary : _bgmSecondary;
        private AudioSource OtherBgm() => _isPrimaryActive ? _bgmSecondary : _bgmPrimary;
        private float EffectiveBgmVolume() => _isMuted ? 0f : _masterVolume * _bgmVolume;
        private float EffectiveSeVolume() => _isMuted ? 0f : _masterVolume * _seVolume;

        private void ApplyBgmVolume()
        {
            if (_isCrossfading) return;
            if (_bgmPrimary != null && _bgmPrimary.isPlaying) _bgmPrimary.volume = EffectiveBgmVolume();
            if (_bgmSecondary != null && _bgmSecondary.isPlaying) _bgmSecondary.volume = EffectiveBgmVolume();
            if (_bgmIntro != null && _bgmIntro.isPlaying) _bgmIntro.volume = EffectiveBgmVolume();
        }

        private void ReleasePreviousBgm(string newAddress)
        {
            if (!string.IsNullOrEmpty(_currentBgmAddress) && _currentBgmAddress != newAddress)
            {
                // アプリ終了中 (ResourceController 破棄後) は Instance が null を返すため null チェックする
                ResourceController resources = ResourceController.HasInstance ? ResourceController.Instance : null;
                if (resources != null)
                {
                    resources.Release(_currentBgmAddress);
                }
                _currentBgmAddress = null;
            }
            StopIntroIfActive();
        }

        private void StopIntroIfActive()
        {
            if (_bgmIntro != null && _bgmIntro.isPlaying)
            {
                _bgmIntro.Stop();
                _bgmIntro.clip = null;
            }
            if (!string.IsNullOrEmpty(_currentBgmIntroAddress))
            {
                ResourceController.Instance.Release(_currentBgmIntroAddress);
                _currentBgmIntroAddress = null;
            }
        }
    }
}
