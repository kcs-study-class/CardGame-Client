using System.Collections.Generic;

namespace KTC.Poker.Domain
{
    /// <summary>
    /// ハンド中の1座席の状態。HandEngine が所有し、外部からは読み取り専用。
    /// </summary>
    public sealed class SeatState
    {
        public int SeatIndex { get; }

        /// <summary>手元に残っているチップ。</summary>
        public int Stack { get; internal set; }

        /// <summary>現在のストリートで投入したチップ。</summary>
        public int StreetBet { get; internal set; }

        /// <summary>このハンド全体で投入したチップ (ポット計算・サイドポットの基礎)。</summary>
        public int TotalCommitted { get; internal set; }

        public bool HasFolded { get; internal set; }

        /// <summary>オールイン状態 (チップを出し切って参加継続中)。</summary>
        public bool IsAllIn => !HasFolded && Stack == 0 && TotalCommitted > 0;

        /// <summary>直近のフルレイズ以降にアクション済みかどうか (エンジン内部制御用)。</summary>
        internal bool HasActed;

        private readonly Card[] _holeCards = new Card[2];
        public IReadOnlyList<Card> HoleCards => _holeCards;

        internal SeatState(int seatIndex, int stack)
        {
            SeatIndex = seatIndex;
            Stack = stack;
        }

        internal void SetHoleCard(int index, Card card)
        {
            _holeCards[index] = card;
        }
    }
}
