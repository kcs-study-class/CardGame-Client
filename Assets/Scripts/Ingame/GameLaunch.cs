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

        /// <summary>直近の対戦のバイイン額 (収支表示用)。バイインなしの対戦 (デバッグ起動) は 0。</summary>
        public static int LastBuyIn;

        /// <summary>直近の対戦で獲得したXP (Result 表示用)。</summary>
        public static long LastXpGained;

        /// <summary>直近の対戦でレベルアップしたか (Result 表示用)。</summary>
        public static bool LastLeveledUp;

        /// <summary>
        /// バイインを所持チップから差し引いて開始した対戦か。
        /// Lobby が差し引き後に true にし、InGameTable の精算 (最終スタックの書き戻し) で false に戻る。
        /// デバッグ起動などバイインなしの対戦では精算しない (無からチップが湧くのを防ぐ)。
        /// サーバー対戦ではバイイン/精算ともサーバー側の責務になる。
        /// </summary>
        public static bool ChipsAtStake;

        /// <summary>Result 表示後のクリア。</summary>
        public static void ClearResult()
        {
            LastFinalState = null;
            LastMySeat = 0;
            LastBuyIn = 0;
            LastXpGained = 0;
            LastLeveledUp = false;
        }
    }
}
