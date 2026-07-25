using System;
using System.Linq;
using KTC.Poker.Domain;
using KTC.Poker.Session;

namespace KTC.Scene
{
    /// <summary>
    /// デバッグメニューから操作するカードゲーム系の設定。
    /// Lobby が対戦開始時に <see cref="Apply"/> で卓設定へ反映する。
    /// </summary>
    public static class DebugGameSettings
    {
        /// <summary>0 以外なら乱数シードを固定 (リプレイ可能な対戦になる)。</summary>
        public static int FixedSeed;

        /// <summary>"As Kd 2c ..." 形式の積み込みデッキ。空なら通常シャッフル。</summary>
        public static string RiggedDeckText = "";

        /// <summary>CPU の手札を公開する (リダクション無効化)。</summary>
        public static bool RevealCpuCards;

        /// <summary>自分の手番を自動でチェック/コールする (モンキーテスト)。</summary>
        public static bool AutoPlay;

        /// <summary>デバッグ設定を卓設定に反映する。</summary>
        public static void Apply(LocalGameSessionConfig config)
        {
            if (FixedSeed != 0)
            {
                config.RandomSeed = FixedSeed;
            }
            config.RevealAllHoleCards = RevealCpuCards;

            var deckText = RiggedDeckText;
            if (!string.IsNullOrWhiteSpace(deckText))
            {
                // 不正表記はここで例外になり Lobby 側のログで気づける
                var cards = deckText.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Card.Parse)
                    .ToArray();
                config.DeckFactory = () => new Deck(cards);
            }
        }
    }
}
