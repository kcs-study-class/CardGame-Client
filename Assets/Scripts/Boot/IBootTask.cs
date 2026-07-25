using System;
using System.Collections.Generic;
using System.Threading;
using KTC.SaveData;
using UnityEngine;

namespace KTC.Boot
{
    /// <summary>
    /// タイトル画面の裏で走るブート処理1つ分の契約。
    /// ログイン・お知らせ取得などのサーバー系タスクは、生徒課題で
    /// スタブ実装を実通信 (CardGame-Server/Docs/API.md) に差し替える。
    /// </summary>
    public interface IBootTask
    {
        /// <summary>進行表示に使う名前 (例: "ログイン")。</summary>
        string DisplayName { get; }

        /// <summary>
        /// タスクを実行する。失敗は例外ではなく <see cref="BootTaskResult.Fail"/> で返す
        /// (例外はランナーが握りつぶして Fail 扱いにする)。
        /// </summary>
        Awaitable<BootTaskResult> RunAsync(BootContext context, CancellationToken cancellationToken);
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

    /// <summary>
    /// ブート処理全体の共有コンテキスト。タスク間の受け渡しに使う
    /// (前のタスクの成果を次のタスクが読む)。
    /// </summary>
    public sealed class BootContext
    {
        public PlayerData PlayerData;

        public bool IsLoggedIn;
        public string AuthToken = "";

        public bool MaintenanceActive;
        public string MaintenanceMessage = "";

        public IReadOnlyList<string> NoticeTitles = Array.Empty<string>();
    }
}
