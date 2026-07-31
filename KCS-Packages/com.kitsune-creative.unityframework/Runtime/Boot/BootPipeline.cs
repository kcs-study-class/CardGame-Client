using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UnityFramework.Boot
{
    /// <summary>
    /// 起動処理1つ分の契約。TContext はゲーム側が定義する共有コンテキスト
    /// (タスク間の受け渡し用。前のタスクの成果を次のタスクが読む)。
    /// </summary>
    public interface IBootTask<in TContext>
    {
        /// <summary>進行表示に使う名前 (例: "ログイン")。</summary>
        string DisplayName { get; }

        /// <summary>
        /// タスクを実行する。失敗は例外ではなく <see cref="BootTaskResult.Fail"/> で返す
        /// (例外はランナーが握りつぶして Fail 扱いにする)。
        /// </summary>
        Awaitable<BootTaskResult> RunAsync(TContext context, CancellationToken cancellationToken);
    }

    /// <summary>ブートタスク1つの結果。</summary>
    public readonly struct BootTaskResult
    {
        public bool Success { get; }
        public string Message { get; }

        private BootTaskResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static BootTaskResult Ok() => new BootTaskResult(true, "");
        public static BootTaskResult Fail(string message) => new BootTaskResult(false, message ?? "");
    }

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
    /// ゲーム側は具象コンテキスト型でこのクラスを継承するか、そのまま使う。
    /// </summary>
    public class BootPipeline<TContext>
    {
        private readonly IReadOnlyList<IBootTask<TContext>> _tasks;
        private int _resumeIndex;

        /// <summary>タスク開始時に発火 (index, total, displayName)。進行表示用。</summary>
        public event Action<int, int, string> TaskStarted;

        public BootPipeline(IReadOnlyList<IBootTask<TContext>> tasks)
        {
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public async Awaitable<BootResult> RunAsync(TContext context, CancellationToken cancellationToken)
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
