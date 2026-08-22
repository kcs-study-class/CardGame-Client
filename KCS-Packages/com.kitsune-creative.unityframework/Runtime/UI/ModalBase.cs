using System.Threading;
using UnityEngine;

namespace UnityFramework.UI
{
    /// <summary>
    /// モーダルプレハブのルートに付ける基底クラス。
    /// <see cref="ModalController.OpenAsync{T}"/> でロード・生成され、
    /// <see cref="CloseAsync"/> で閉じる。開閉演出は OnOpenAsync / OnCloseAsync を
    /// オーバーライドして実装する (既定は演出なし)。
    /// </summary>
    public abstract class ModalBase : MonoBehaviourBase
    {
        private ModalController _owner = null;
        private AwaitableCompletionSource _closedSource = null;

        /// <summary>ロード元の Addressables アドレス (解放時に使用)。</summary>
        internal string Address { get; private set; }

        /// <summary>閉じ処理が完了したかどうか。</summary>
        public bool IsClosed { get; private set; }

        /// <summary>背景 (ディム) クリックで閉じるかどうか。既定は false。</summary>
        protected internal virtual bool CloseOnBackdropClick => false;

        internal void Setup(ModalController owner, string address)
        {
            _owner = owner;
            Address = address;
            _closedSource = new AwaitableCompletionSource();
        }

        /// <summary>このモーダルを閉じる (演出 → 破棄 → アセット解放)。</summary>
        public Awaitable CloseAsync(CancellationToken cancellationToken = default)
        {
            return _owner != null ? _owner.CloseAsync(this, cancellationToken) : Awaitables.Completed;
        }

        /// <summary>
        /// 閉じられるまで待機する。「開いて結果を待つ」フローで利用する。
        /// (Awaitable の制約により await できるのは1箇所のみ)
        /// </summary>
        public Awaitable WaitUntilClosedAsync()
        {
            return _closedSource.Awaitable;
        }

        internal Awaitable PlayOpenAsync(CancellationToken cancellationToken)
        {
            return OnOpenAsync(cancellationToken);
        }

        internal async Awaitable PlayCloseAsync(CancellationToken cancellationToken)
        {
            await OnCloseAsync(cancellationToken);
            IsClosed = true;
            _closedSource.TrySetResult();
        }

        /// <summary>開く演出。既定は即完了。</summary>
        protected virtual Awaitable OnOpenAsync(CancellationToken cancellationToken) => Awaitables.Completed;

        /// <summary>閉じる演出。既定は即完了。</summary>
        protected virtual Awaitable OnCloseAsync(CancellationToken cancellationToken) => Awaitables.Completed;
    }
}
