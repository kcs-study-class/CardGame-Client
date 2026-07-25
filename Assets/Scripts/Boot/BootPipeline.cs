using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityFramework;

namespace KTC.Boot
{
    /// <summary>ブートパイプライン全体の結果。</summary>
    public readonly struct BootResult
    {
        public bool Success { get; }
        public string FailedTaskName { get; }
        public string Message { get; }

        private BootResult(bool success, string failedTaskName, string message)
        {
            Success = success;
            FailedTaskName = failedTaskName;
            Message = message;
        }

        public static BootResult Ok() => new BootResult(true, "", "");
        public static BootResult Fail(string taskName, string message) => new BootResult(false, taskName, message ?? "");
    }

    /// <summary>
    /// ブートタスクを登録順に逐次実行するランナー。
    /// 失敗した場合はそこで停止し、再度 <see cref="RunAsync"/> を呼ぶと
    /// **失敗したタスクから** 再開する (成功済みタスクはやり直さない)。
    /// </summary>
    public sealed class BootPipeline
    {
        private readonly IReadOnlyList<IBootTask> _tasks;
        private int _resumeIndex;

        /// <summary>タスク開始時に発火 (index, total, displayName)。進行表示用。</summary>
        public event Action<int, int, string> TaskStarted;

        public BootPipeline(IReadOnlyList<IBootTask> tasks)
        {
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public async Awaitable<BootResult> RunAsync(BootContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            for (int i = _resumeIndex; i < _tasks.Count; i++)
            {
                var task = _tasks[i];
                cancellationToken.ThrowIfCancellationRequested();
                TaskStarted?.Invoke(i, _tasks.Count, task.DisplayName);

                BootTaskResult result;
                try
                {
                    result = await task.RunAsync(context, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    SafeLogger.LogError($"[BootPipeline] '{task.DisplayName}' が例外で失敗: {e.Message}");
                    result = BootTaskResult.Fail(e.Message);
                }

                if (!result.Success)
                {
                    _resumeIndex = i; // リトライは失敗タスクから
                    return BootResult.Fail(task.DisplayName, result.Message);
                }
            }

            _resumeIndex = _tasks.Count;
            return BootResult.Ok();
        }
    }
}
