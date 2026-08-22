using System.Threading;
using UnityEngine;

namespace UnityFramework.SceneManagement
{
    /// <summary>
    /// シーンコントローラの基底クラス。<see cref="IScenePreparer"/> の定型
    /// (準備の1回実行保証・エディタ直接再生時のフォールバック・遷移の二重起動防止) を提供する。
    ///
    /// 派生クラスは <see cref="OnPrepareAsync"/> に準備処理 (アセットロード/UI構築) を書く。
    /// SceneController 経由なら フェードインの前に、エディタで直接再生した場合は Start で呼ばれる。
    /// </summary>
    public abstract class SceneBase : MonoBehaviourBase, IScenePreparer
    {
        private bool _isPrepared = false;
        private bool _isTransitioning = false;

        /// <summary>準備処理が完了 (または開始) しているか。</summary>
        protected bool IsPrepared => _isPrepared;

        /// <summary>シーン遷移を開始済みか (入力の二重受付防止に使う)。</summary>
        protected bool IsTransitioning => _isTransitioning;

        /// <summary>フェードインで見せる前の準備。2回目以降の呼び出しは無視する。</summary>
        public async Awaitable PrepareAsync(CancellationToken cancellationToken)
        {
            if (_isPrepared)
            {
                return;
            }
            _isPrepared = true;
            await OnPrepareAsync(cancellationToken);
        }

        /// <summary>シーン固有の準備処理 (アセットロード / UI 構築 / BGM 等)。</summary>
        protected abstract Awaitable OnPrepareAsync(CancellationToken cancellationToken);

        private async void Start()
        {
            await OnStartAsync();
        }

        /// <summary>
        /// Start 時の処理。既定は「SceneController を経由しない直接再生 (エディタ) 用のフォールバック」で、
        /// 未準備なら 1 フレーム後に <see cref="PrepareAsync"/> を呼ぶ。
        /// </summary>
        protected virtual async Awaitable OnStartAsync()
        {
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (!_isPrepared)
            {
                await PrepareAsync(destroyCancellationToken);
            }
        }

        /// <summary>遷移を開始する。既に遷移中なら false (呼び出し側は何もしない)。</summary>
        protected bool TryBeginTransition()
        {
            if (_isTransitioning)
            {
                return false;
            }
            _isTransitioning = true;
            return true;
        }

        /// <summary>遷移を取りやめる (モーダル失敗などでシーンに留まる場合)。</summary>
        protected void CancelTransition()
        {
            _isTransitioning = false;
        }
    }
}
