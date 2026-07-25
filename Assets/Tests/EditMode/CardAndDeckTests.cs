using System;
using System.Collections.Generic;
using KTC.Poker.Domain;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    public class CardAndDeckTests
    {
        [Test]
        public void カード表記の相互変換()
        {
            Assert.That(Card.Parse("As").ToString(), Is.EqualTo("As"));
            Assert.That(Card.Parse("td").ToString(), Is.EqualTo("Td"), "小文字ランクも許容");
            Assert.That(Card.Parse("2C").ToString(), Is.EqualTo("2c"), "大文字スートも許容");
            Assert.That(Card.Parse("Kh"), Is.EqualTo(new Card(Rank.King, Suit.Heart)));
        }

        [Test]
        public void byte値へのパックと復元()
        {
            // value = (suit << 4) | rank  (MemoryCardGame CardId 準拠レイアウト、ただし A=14)
            Assert.That(Card.Parse("As").Value, Is.EqualTo((byte)14), "As = (0<<4)|14");
            Assert.That(Card.Parse("2c").Value, Is.EqualTo((byte)50), "2c = (3<<4)|2");
            Assert.That(Card.Parse("Td").Value, Is.EqualTo((byte)42), "Td = (2<<4)|10");
            Assert.That(Card.Parse("Ah").Value, Is.EqualTo((byte)30), "Ah = (1<<4)|14");

            // マスク定数でも分解できる
            var kc = Card.Parse("Kc");
            Assert.That(kc.Value & Card.RankMask, Is.EqualTo(13));
            Assert.That((kc.Value & Card.SuitMask) >> Card.SuitShift, Is.EqualTo((int)Suit.Club));

            // 52枚全てラウンドトリップ
            var deck = new Deck();
            while (deck.Remaining > 0)
            {
                var card = deck.Draw();
                Assert.That(Card.FromValue(card.Value), Is.EqualTo(card));
            }
        }

        [Test]
        public void 不正なbyte値は復元できない()
        {
            Assert.That(Card.TryFromValue(0, out var none), Is.True, "0 は None として許容");
            Assert.That(none.IsNone, Is.True);
            Assert.That(none.ToString(), Is.EqualTo("??"));
            Assert.That(Card.TryFromValue(1, out _), Is.False, "rank 1 は不正 (ポーカーでは A=14)");
            Assert.That(Card.TryFromValue(15, out _), Is.False, "rank 15 は不正");
            Assert.That(Card.TryFromValue(0b0100_0000, out _), Is.False, "予約ビット (bit6) は不正");
            Assert.That(Card.TryFromValue(0b1000_0010, out _), Is.False, "予約ビット (bit7) は不正");
            Assert.That(() => Card.FromValue(255), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void 記号付き表示文字列()
        {
            Assert.That(Card.Parse("As").ToSymbolString(), Is.EqualTo("A♠"));
            Assert.That(Card.Parse("Th").ToSymbolString(), Is.EqualTo("10♥"));
            Assert.That(Card.Parse("2d").ToSymbolString(), Is.EqualTo("2♦"));
            Assert.That(Card.Parse("Qc").ToSymbolString(), Is.EqualTo("Q♣"));
            Assert.That(Card.Parse("Ah").IsRedSuit, Is.True);
            Assert.That(Card.Parse("As").IsRedSuit, Is.False);
        }

        [Test]
        public void 未公開カードは役評価に渡せない()
        {
            var cards = new[]
            {
                Card.None, Card.Parse("Kd"), Card.Parse("Qh"), Card.Parse("Js"), Card.Parse("9c"),
            };
            Assert.That(() => KTC.Poker.Domain.HandEvaluator.Evaluate(cards), Throws.ArgumentException);
        }

        [Test]
        public void 不正なカード表記はTryParseがfalse()
        {
            Assert.That(Card.TryParse("Xx", out _), Is.False);
            Assert.That(Card.TryParse("A", out _), Is.False);
            Assert.That(Card.TryParse(null, out _), Is.False);
            Assert.That(() => Card.Parse("1s"), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void デッキは52枚すべてユニーク()
        {
            var deck = new Deck();
            deck.Shuffle(new Random(1));
            var seen = new HashSet<Card>();
            while (deck.Remaining > 0)
            {
                Assert.That(seen.Add(deck.Draw()), Is.True, "重複カードが出た");
            }
            Assert.That(seen.Count, Is.EqualTo(52));
        }

        [Test]
        public void 同じシードなら同じ並びになる()
        {
            var a = new Deck();
            var b = new Deck();
            a.Shuffle(new Random(42));
            b.Shuffle(new Random(42));
            for (int i = 0; i < 52; i++)
            {
                Assert.That(a.Draw(), Is.EqualTo(b.Draw()));
            }
        }

        [Test]
        public void 空のデッキから引くと例外()
        {
            var deck = new Deck();
            for (int i = 0; i < 52; i++) deck.Draw();
            Assert.That(() => deck.Draw(), Throws.InvalidOperationException);
        }
    }
}
