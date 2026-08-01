using System.Collections.Generic;
using UnityFramework.Boot;

namespace KTC.Boot
{
    /// <summary>
    /// <see cref="BootContext"/> 固定のパイプライン (ゲーム側の使い勝手用)。
    /// 逐次実行・失敗タスクからの再開などの本体は UnityFramework.Boot.BootPipeline&lt;T&gt; を参照。
    /// </summary>
    public sealed class BootPipeline : BootPipeline<BootContext>
    {
        public BootPipeline(IReadOnlyList<IBootTask> tasks) : base(tasks)
        {
        }
    }
}
