using System;
using System.Collections.Generic;

namespace KTC.Poker.Domain
{
    /// <summary>役のカテゴリ。数値が大きいほど強い。</summary>
    public enum HandCategory
    {
        HighCard = 0,
        OnePair = 1,
        TwoPair = 2,
        ThreeOfAKind = 3,
        Straight = 4,
        Flush = 5,
        FullHouse = 6,
        FourOfAKind = 7,
        StraightFlush = 8,
    }

    public static class HandCategoryExtensions
    {
        /// <summary>UI表示用の日本語名。</summary>
        public static string ToDisplayName(this HandCategory category) => category switch
        {
            HandCategory.HighCard => "ハイカード",
            HandCategory.OnePair => "ワンペア",
            HandCategory.TwoPair => "ツーペア",
            HandCategory.ThreeOfAKind => "スリーカード",
            HandCategory.Straight => "ストレート",
            HandCategory.Flush => "フラッシュ",
            HandCategory.FullHouse => "フルハウス",
            HandCategory.FourOfAKind => "フォーカード",
            HandCategory.StraightFlush => "ストレートフラッシュ",
            _ => category.ToString(),
        };
    }

    /// <summary>
    /// 評価済みの役の強さ。カテゴリと最大5つのタイブレークランクを
    /// 1つの int にパックしているため、単純比較だけで勝敗が決まる。
    /// レイアウト: [カテゴリ:4bit][t1:4bit][t2:4bit][t3:4bit][t4:4bit][t5:4bit]
    /// (ランクは 2..14 なので 4bit に収まる)
    /// </summary>
    public readonly struct HandValue : IComparable<HandValue>, IEquatable<HandValue>
    {
        private readonly int _score;

        private HandValue(int score)
        {
            _score = score;
        }

        internal static HandValue Create(HandCategory category, int t1 = 0, int t2 = 0, int t3 = 0, int t4 = 0, int t5 = 0)
        {
            int score = ((int)category << 20) | (t1 << 16) | (t2 << 12) | (t3 << 8) | (t4 << 4) | t5;
            return new HandValue(score);
        }

        public HandCategory Category => (HandCategory)(_score >> 20);

        /// <summary>タイブレークランク (強い順、未使用スロットは 0)。表示・デバッグ用。</summary>
        public IReadOnlyList<int> Tiebreaks => new[]
        {
            (_score >> 16) & 0xF,
            (_score >> 12) & 0xF,
            (_score >> 8) & 0xF,
            (_score >> 4) & 0xF,
            _score & 0xF,
        };

        /// <summary>ロイヤルフラッシュ (A ハイのストレートフラッシュ) かどうか。表示用。</summary>
        public bool IsRoyalFlush => Category == HandCategory.StraightFlush && ((_score >> 16) & 0xF) == (int)Rank.Ace;

        /// <summary>UI表示用の日本語名 (ロイヤルフラッシュのみ特別扱い)。</summary>
        public string DisplayName => IsRoyalFlush ? "ロイヤルフラッシュ" : Category.ToDisplayName();

        public int CompareTo(HandValue other) => _score.CompareTo(other._score);
        public bool Equals(HandValue other) => _score == other._score;
        public override bool Equals(object obj) => obj is HandValue other && Equals(other);
        public override int GetHashCode() => _score;

        public static bool operator >(HandValue left, HandValue right) => left._score > right._score;
        public static bool operator <(HandValue left, HandValue right) => left._score < right._score;
        public static bool operator >=(HandValue left, HandValue right) => left._score >= right._score;
        public static bool operator <=(HandValue left, HandValue right) => left._score <= right._score;
        public static bool operator ==(HandValue left, HandValue right) => left.Equals(right);
        public static bool operator !=(HandValue left, HandValue right) => !left.Equals(right);

        public override string ToString()
        {
            return $"{Category}[{string.Join(",", Tiebreaks)}]";
        }
    }
}
