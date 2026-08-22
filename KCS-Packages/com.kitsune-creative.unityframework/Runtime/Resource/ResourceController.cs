using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UObject = UnityEngine.Object;

namespace UnityFramework.Resource
{
    /// <summary>
    /// Addressables ベースの非同期アセットローダー。
    /// 同一アドレスへの多重ロードは内部キャッシュで合流し、参照カウントで管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceController : SingletonMonoBehaviour<ResourceController>
    {
        private sealed class Entry
        {
            public AsyncOperationHandle Handle = default;
            public int RefCount = 0;
        }

        private readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

        /// <summary>
        /// 指定アドレスのアセットがロード済みかどうか (キャッシュヒット可否)。
        /// </summary>
        public bool IsLoaded(string address)
        {
            return _cache.TryGetValue(address, out Entry entry)
                && entry.Handle.IsValid()
                && entry.Handle.IsDone
                && entry.Handle.Status == AsyncOperationStatus.Succeeded;
        }

        /// <summary>
        /// キャッシュ済みのアセットを同期取得する。未ロードまたはロード中は null。
        /// </summary>
        public T Get<T>(string address) where T : UObject
        {
            if (!_cache.TryGetValue(address, out Entry entry)) return null;
            if (!entry.Handle.IsValid() || !entry.Handle.IsDone) return null;
            return entry.Handle.Result as T;
        }

        /// <summary>
        /// 指定アドレスを非同期にロードする。多重呼び出しは合流し、参照カウントが+1される。
        /// </summary>
        public async Awaitable<T> LoadAsync<T>(string address, CancellationToken cancellationToken = default) where T : UObject
        {
            if (string.IsNullOrEmpty(address))
            {
                SafeLogger.LogError("[ResourceController] address が null または空文字です。null を返します。");
                return null;
            }

            if (_cache.TryGetValue(address, out Entry entry))
            {
                entry.RefCount++;
                if (!entry.Handle.IsDone)
                {
                    await WaitHandleAsync(entry.Handle, cancellationToken);
                }
                if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
                {
                    _cache.Remove(address);
                    if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
                    SafeLogger.LogError($"[ResourceController] ロード失敗 (キャッシュエントリ): {address}。null を返します。");
                    return null;
                }
                return entry.Handle.Result as T;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
            entry = new Entry { Handle = handle, RefCount = 1 };
            _cache[address] = entry;

            try
            {
                await WaitHandleAsync(handle, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cache.Remove(address);
                if (handle.IsValid()) Addressables.Release(handle);
                throw;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                _cache.Remove(address);
                if (handle.IsValid()) Addressables.Release(handle);
                SafeLogger.LogError($"[ResourceController] ロード失敗: {address}。null を返します。");
                return null;
            }

            return handle.Result as T;
        }

        /// <summary>
        /// アセットを事前ロード (キャッシュへの先読み)。参照カウントを+1する。
        /// 使用が終わったら <see cref="Release"/> で解放すること。
        /// </summary>
        public async Awaitable PreloadAsync<T>(string address, CancellationToken cancellationToken = default) where T : UObject
        {
            await LoadAsync<T>(address, cancellationToken);
        }

        /// <summary>
        /// 複数アセットを並列に事前ロードする。各アドレスの参照カウントが+1される。
        /// </summary>
        public async Awaitable PreloadAllAsync<T>(IEnumerable<string> addresses, CancellationToken cancellationToken = default) where T : UObject
        {
            if (addresses == null) return;
            List<Awaitable<T>> pending = new List<Awaitable<T>>();
            foreach (string address in addresses)
            {
                pending.Add(LoadAsync<T>(address, cancellationToken));
            }
            foreach (Awaitable<T> task in pending)
            {
                await task;
            }
        }

        /// <summary>
        /// 参照カウントを-1し、0 になったら解放する。
        /// </summary>
        public void Release(string address)
        {
            if (!_cache.TryGetValue(address, out Entry entry)) return;
            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
                _cache.Remove(address);
            }
        }

        /// <summary>
        /// 全エントリを強制解放する。シーン切替やアプリ終了時に利用。
        /// </summary>
        public void ReleaseAll()
        {
            foreach (Entry entry in _cache.Values)
            {
                if (entry.Handle.IsValid()) Addressables.Release(entry.Handle);
            }
            _cache.Clear();
        }

        private static async Awaitable WaitHandleAsync(AsyncOperationHandle handle, CancellationToken cancellationToken)
        {
            while (!handle.IsDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        protected override void OnDestroy()
        {
            ReleaseAll();
            base.OnDestroy();
        }
    }
}
