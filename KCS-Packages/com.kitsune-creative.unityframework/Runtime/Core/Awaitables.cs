using UnityEngine;

namespace UnityFramework
{
    /// <summary>
    /// <see cref="Awaitable"/> ユーティリティ。
    /// エラー時の早期 return で「即座に完了した Awaitable」を返したい場面で使う。
    /// </summary>
    public static class Awaitables
    {
        /// <summary>即座に完了する <see cref="Awaitable"/> を返す。</summary>
        public static Awaitable Completed
        {
            get
            {
                AwaitableCompletionSource src = new AwaitableCompletionSource();
                src.SetResult();
                return src.Awaitable;
            }
        }

        /// <summary>指定値で即座に完了する <see cref="Awaitable{T}"/> を返す。</summary>
        public static Awaitable<T> FromResult<T>(T value)
        {
            AwaitableCompletionSource<T> src = new AwaitableCompletionSource<T>();
            src.SetResult(value);
            return src.Awaitable;
        }
    }
}
