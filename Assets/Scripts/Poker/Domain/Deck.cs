using System;
using System.Collections.Generic;

namespace KTC.Poker.Domain
{
    /// <summary>
    /// 52枚の山札。シャッフルは Fisher-Yates。
    /// Random を注入できるため、シード固定でテスト・リプレイが可能。
    /// </summary>
    public sealed class Deck
    {
        private readonly List<Card> _cards = new List<Card>(52);
        private int _next = 0;

        /// <summary>スート順・ランク順に整列した状態で生成する。</summary>
        public Deck()
        {
            for (int suit = 0; suit <= 3; suit++)
            {
                for (int rank = 2; rank <= 14; rank++)
                {
                    _cards.Add(new Card((Rank)rank, (Suit)suit));
                }
            }
        }

        /// <summary>
        /// テスト・リプレイ用: 指定した並びのデッキを作る (先頭が最初に配られる)。
        /// 52枚未満でも良いが、重複と未公開カード (None) は拒否する。
        /// </summary>
        public Deck(IReadOnlyList<Card> orderedCards)
        {
            if (orderedCards == null)
            {
                throw new ArgumentNullException(nameof(orderedCards));
            }
            HashSet<Card> seen = new HashSet<Card>();
            foreach (Card card in orderedCards)
            {
                if (card.IsNone)
                {
                    throw new ArgumentException("None はデッキに含められません。", nameof(orderedCards));
                }
                if (!seen.Add(card))
                {
                    throw new ArgumentException($"カードが重複しています: {card}", nameof(orderedCards));
                }
                _cards.Add(card);
            }
        }

        /// <summary>残り枚数。</summary>
        public int Remaining => _cards.Count - _next;

        /// <summary>Fisher-Yates でシャッフルし、配布位置を先頭に戻す。</summary>
        public void Shuffle(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
            _next = 0;
        }

        /// <summary>山札の先頭から1枚引く。</summary>
        public Card Draw()
        {
            if (Remaining <= 0)
            {
                throw new InvalidOperationException("山札が空です。");
            }
            return _cards[_next++];
        }
    }
}
