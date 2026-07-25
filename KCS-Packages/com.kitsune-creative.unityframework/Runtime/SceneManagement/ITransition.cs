using System.Threading;
using UnityEngine;

namespace UnityFramework.SceneManagement
{
    /// <summary>
    /// カスタムトランジション演出のインターフェイス。
    /// <see cref="TransitionController.CustomTransition"/> に登録するとフェードの代わりに利用される。
    /// </summary>
    public interface ITransition
    {
        /// <summary>遷移開始時の演出 (画面を覆う側)。</summary>
        Awaitable PlayOutAsync(CancellationToken cancellationToken);

        /// <summary>遷移完了時の演出 (覆いを取り除く側)。</summary>
        Awaitable PlayInAsync(CancellationToken cancellationToken);
    }
}
