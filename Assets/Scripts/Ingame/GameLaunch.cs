using KTC.Poker.Protocol;
using KTC.Poker.Session;

namespace KTC.Scene
{
    /// <summary>
    /// シーンをまたいで対戦設定と結果を受け渡す入れ物。
    /// Lobby → InGame: <see cref="NextConfig"/> / InGame → Result: <see cref="LastFinalState"/>
    /// (サーバー対戦になってもこの受け渡し構造は変わらない想定)
    /// </summary>
    public static class GameLaunch
    {
        /// <summary>次の対戦の卓設定 (Lobby が設定し InGame が消費する)。null なら既定値。</summary>
        public static LocalGameSessionConfig NextConfig;

        /// <summary>直近の対戦の最終スナップショット (Result 表示用)。</summary>
        public static TableStateMessage LastFinalState;

        /// <summary>直近の対戦で自分が座っていた席。</summary>
        public static int LastMySeat;

        /// <summary>Result 表示後のクリア。</summary>
        public static void ClearResult()
        {
            LastFinalState = null;
            LastMySeat = 0;
        }
    }
}
