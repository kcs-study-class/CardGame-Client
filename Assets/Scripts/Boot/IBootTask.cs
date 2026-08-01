using System;
using System.Collections.Generic;
using KTC.SaveData;
using UnityFramework.Boot;

namespace KTC.Boot
{
    /// <summary>
    /// このゲームのブートタスク契約 (共有コンテキスト = <see cref="BootContext"/>)。
    /// パイプライン本体は UnityFramework.Boot に汎用実装がある。
    /// ログイン・お知らせ取得などのサーバー系タスクは、生徒課題で
    /// スタブ実装を実通信 (CardGame-Server/Docs/API.md) に差し替える。
    /// </summary>
    public interface IBootTask : IBootTask<BootContext>
    {
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
