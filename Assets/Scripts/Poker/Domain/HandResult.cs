using System.Collections.Generic;

namespace KTC.Poker.Domain
{
    /// <summary>1つのポット (メイン/サイド) の分配結果。</summary>
    public sealed class PotResult
    {
        /// <summary>このポットの総額。</summary>
        public int Amount { get; }

        /// <summary>このポットの獲得権があった座席。</summary>
        public IReadOnlyList<int> EligibleSeats { get; }

        /// <summary>勝者の座席 (スプリット時は複数)。</summary>
        public IReadOnlyList<int> WinnerSeats { get; }

        internal PotResult(int amount, IReadOnlyList<int> eligibleSeats, IReadOnlyList<int> winnerSeats)
        {
            Amount = amount;
            EligibleSeats = eligibleSeats;
            WinnerSeats = winnerSeats;
        }
    }

    /// <summary>ハンド終了時の確定結果。</summary>
    public sealed class HandResult
    {
        /// <summary>メインポット → サイドポットの順。</summary>
        public IReadOnlyList<PotResult> Pots { get; }

        /// <summary>座席ごとの獲得額 (獲得なしは 0)。</summary>
        public IReadOnlyList<int> Payouts { get; }

        /// <summary>ハンド終了後の各座席のスタック。</summary>
        public IReadOnlyList<int> FinalStacks { get; }

        /// <summary>ショーダウンまで行ったか (false = フォールドによる決着)。</summary>
        public bool WentToShowdown { get; }

        /// <summary>ショーダウン参加者の最終役 (フォールド決着時は空)。</summary>
        public IReadOnlyDictionary<int, HandValue> ShowdownHands { get; }

        internal HandResult(
            IReadOnlyList<PotResult> pots,
            IReadOnlyList<int> payouts,
            IReadOnlyList<int> finalStacks,
            bool wentToShowdown,
            IReadOnlyDictionary<int, HandValue> showdownHands)
        {
            Pots = pots;
            Payouts = payouts;
            FinalStacks = finalStacks;
            WentToShowdown = wentToShowdown;
            ShowdownHands = showdownHands;
        }
    }
}
