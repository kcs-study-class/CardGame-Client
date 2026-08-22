using System;

namespace KTC.Poker.Domain
{
    /// <summary>
    /// トランプのスート (マーク)。bit 4..5 に格納される。
    /// 値の割り当ては MemoryCardGame の CardId と同じ (♠=0, ♥=1, ♦=2, ♣=3)。
    /// </summary>
    public enum Suit : byte
    {
        Spade = 0,
        Heart = 1,
        Diamond = 2,
        Club = 3,
    }

    /// <summary>
    /// ランク。数値はポーカーの強さ (2 が最弱、Ace = 14 が最強)。
    /// MemoryCardGame (A=1) とは異なり、ポーカーでは A を 14 として扱う。
    /// ストレート A-2-3-4-5 (ホイール) の場合のみ A を 1 相当として扱う (HandEvaluator 側で処理)。
    /// </summary>
    public enum Rank
    {
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14,
    }

    /// <summary>
    /// 不変のトランプカード。内部表現は 1 byte のパック値で、
    /// ビットレイアウトは MemoryCardGame の CardId に準拠する:
    /// <code>
    ///   bit:  7 6 5 4 3 2 1 0
    ///         | | | | | | | |
    ///         | | | | +-+-+-+-- Rank (2..14: 2..10,J,Q,K,A=14) — 4 bit
    ///         | | +-+---------- Suit (0..3: ♠♥♦♣)              — 2 bit
    ///         +-+-------------- 予約 (常に 0。MemoryCardGame では bit6 = Joker)
    /// </code>
    /// 通信プロトコルでもこの byte 値をそのまま使う (As = 14, 2c = 50)。
    /// 値 0 は「未公開カード (裏面)」の番兵として予約 (<see cref="None"/>)。
    /// "As" / "Td" のような2文字表記はデバッグ・テスト用。
    /// </summary>
    public readonly struct Card : IEquatable<Card>
    {
        public const byte RANK_MASK = 0b0000_1111; // bit 0..3
        public const byte SUIT_MASK = 0b0011_0000; // bit 4..5
        public const int SUIT_SHIFT = 4;

        private readonly byte _value;

        public Card(Rank rank, Suit suit)
        {
            _value = (byte)(((int)suit << SUIT_SHIFT) | (int)rank);
        }

        private Card(byte value)
        {
            _value = value;
        }

        /// <summary>パック済み byte 値。通信ではこの値をそのまま送受信する。</summary>
        public byte Value => _value;

        public Rank Rank => (Rank)(_value & RANK_MASK);
        public Suit Suit => (Suit)((_value & SUIT_MASK) >> SUIT_SHIFT);

        /// <summary>未公開カード (裏面)。byte 値 0。</summary>
        public static Card None => default;

        /// <summary>未公開カードかどうか。</summary>
        public bool IsNone => _value == 0;

        /// <summary>♥ ♦ なら赤、♠ ♣ なら黒。</summary>
        public bool IsRedSuit => Suit == Suit.Heart || Suit == Suit.Diamond;

        /// <summary>
        /// byte 値からカードを復元する。通信の受信側で使う。
        /// 0 は None として許容。予約ビット (bit 6..7) が立っている値、
        /// rank が 2..14 の範囲外の値は不正として拒否する。
        /// </summary>
        public static Card FromValue(byte value)
        {
            if (!TryFromValue(value, out var card))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "カードのbyte値として不正です。");
            }
            return card;
        }

        public static bool TryFromValue(byte value, out Card card)
        {
            card = default;
            if (value == 0)
            {
                return true; // None
            }
            if ((value & ~(RANK_MASK | SUIT_MASK)) != 0)
            {
                return false; // 予約ビットが立っている
            }
            int rank = value & RANK_MASK;
            if (rank < 2 || rank > 14)
            {
                return false;
            }
            card = new Card(value);
            return true;
        }

        private const string RANK_CHARS = "23456789TJQKA";
        // Suit enum の値順 (♠♥♦♣) に対応
        private const string SUIT_CHARS = "shdc";

        /// <summary>"As" / "Td" 形式の2文字表記に変換する。None は "??"。</summary>
        public override string ToString()
        {
            if (IsNone)
            {
                return "??";
            }
            return string.Concat(RANK_CHARS[(int)Rank - 2], SUIT_CHARS[(int)Suit]);
        }

        /// <summary>"A♠" "10♥" 等のUI向け表示文字列。None は "🂠" 相当の "??"。</summary>
        public string ToSymbolString()
        {
            if (IsNone)
            {
                return "??";
            }
            string r = Rank switch
            {
                Rank.Ace => "A",
                Rank.King => "K",
                Rank.Queen => "Q",
                Rank.Jack => "J",
                Rank.Ten => "10",
                _ => ((int)Rank).ToString(),
            };
            string s = Suit switch
            {
                Suit.Spade => "♠",
                Suit.Heart => "♥",
                Suit.Diamond => "♦",
                Suit.Club => "♣",
                _ => "?",
            };
            return r + s;
        }

        /// <summary>"As" / "Td" 形式の2文字表記から生成する。大文字小文字は許容する。</summary>
        public static Card Parse(string text)
        {
            if (!TryParse(text, out var card))
            {
                throw new FormatException($"カード表記として解釈できません: '{text}'");
            }
            return card;
        }

        public static bool TryParse(string text, out Card card)
        {
            card = default;
            if (text == null || text.Length != 2)
            {
                return false;
            }
            int rankIndex = RANK_CHARS.IndexOf(char.ToUpperInvariant(text[0]));
            int suitIndex = SUIT_CHARS.IndexOf(char.ToLowerInvariant(text[1]));
            if (rankIndex < 0 || suitIndex < 0)
            {
                return false;
            }
            card = new Card((Rank)(rankIndex + 2), (Suit)suitIndex);
            return true;
        }

        public bool Equals(Card other) => _value == other._value;
        public override bool Equals(object obj) => obj is Card other && Equals(other);
        public override int GetHashCode() => _value;
        public static bool operator ==(Card left, Card right) => left.Equals(right);
        public static bool operator !=(Card left, Card right) => !left.Equals(right);
    }
}
