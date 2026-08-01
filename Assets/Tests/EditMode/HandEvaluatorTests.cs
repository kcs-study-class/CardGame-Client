using System.Linq;
using KTC.Poker.Domain;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    public class HandEvaluatorTests
    {
        /// <summary>"As Kd ..." のようなスペース区切り表記からカード配列を作る。</summary>
        private static Card[] Cards(string text)
        {
            return text.Split(' ').Select(Card.Parse).ToArray();
        }

        private static HandValue Eval(string text)
        {
            return HandEvaluator.Evaluate(Cards(text));
        }

        // ---- カテゴリ判定 ----

        [TestCase("As Ks Qs Js Ts", HandCategory.StraightFlush, TestName = "ロイヤルフラッシュ")]
        [TestCase("9h 8h 7h 6h 5h", HandCategory.StraightFlush, TestName = "ストレートフラッシュ")]
        [TestCase("Ac Ad Ah As Kc", HandCategory.FourOfAKind, TestName = "フォーカード")]
        [TestCase("Kc Kd Kh 2c 2d", HandCategory.FullHouse, TestName = "フルハウス")]
        [TestCase("Ah Jh 9h 6h 3h", HandCategory.Flush, TestName = "フラッシュ")]
        [TestCase("9c 8d 7h 6s 5c", HandCategory.Straight, TestName = "ストレート")]
        [TestCase("Qc Qd Qh 8s 3c", HandCategory.ThreeOfAKind, TestName = "スリーカード")]
        [TestCase("Jc Jd 4h 4s Ac", HandCategory.TwoPair, TestName = "ツーペア")]
        [TestCase("Tc Td Ah 7s 2c", HandCategory.OnePair, TestName = "ワンペア")]
        [TestCase("Ac Jd 9h 6s 3c", HandCategory.HighCard, TestName = "ハイカード")]
        public void カテゴリが正しく判定される(string hand, HandCategory expected)
        {
            Assert.That(Eval(hand).Category, Is.EqualTo(expected));
        }

        [Test]
        public void ロイヤルフラッシュの表示名は特別扱い()
        {
            var royal = Eval("As Ks Qs Js Ts");
            Assert.That(royal.IsRoyalFlush, Is.True);
            Assert.That(royal.DisplayName, Is.EqualTo("ロイヤルフラッシュ"));
            var straightFlush = Eval("9h 8h 7h 6h 5h");
            Assert.That(straightFlush.IsRoyalFlush, Is.False);
        }

        // ---- ストレートの境界 ----

        [Test]
        public void ホイールは5ハイのストレート()
        {
            var wheel = Eval("Ah 2c 3d 4s 5h");
            Assert.That(wheel.Category, Is.EqualTo(HandCategory.Straight));
            var sixHigh = Eval("2h 3c 4d 5s 6h");
            Assert.That(sixHigh, Is.GreaterThan(wheel), "6ハイストレートはホイールに勝つ");
        }

        [Test]
        public void エースハイストレートが最強のストレート()
        {
            var broadway = Eval("Ac Kd Qh Js Tc");
            var kingHigh = Eval("Kc Qd Jh Ts 9c");
            Assert.That(broadway.Category, Is.EqualTo(HandCategory.Straight));
            Assert.That(broadway, Is.GreaterThan(kingHigh));
        }

        [Test]
        public void AKQJ9はストレートではない()
        {
            Assert.That(Eval("Ac Kd Qh Js 9c").Category, Is.EqualTo(HandCategory.HighCard));
        }

        // ---- キッカー勝負 ----

        [Test]
        public void 同じペアはキッカーで決まる()
        {
            var kickerAce = Eval("Tc Td Ah 7s 2c");
            var kickerKing = Eval("Th Ts Kh 7d 2d");
            Assert.That(kickerAce, Is.GreaterThan(kickerKing));
        }

        [Test]
        public void 同ランク構成のペアは引き分け()
        {
            var a = Eval("Tc Td Ah 7s 2c");
            var b = Eval("Th Ts Ad 7c 2d");
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void フラッシュは5枚全てで比較する()
        {
            var better = Eval("Ah Jh 9h 6h 4h");
            var worse = Eval("As Js 9s 6s 3s");
            Assert.That(better, Is.GreaterThan(worse));
        }

        [Test]
        public void フォーカードのキッカー勝負()
        {
            // コミュニティに quad がある状況を想定した 7 枚評価
            var kickerAce = HandEvaluator.Evaluate(Cards("8c 8d 8h 8s 2c Ac 3d"));
            var kickerKing = HandEvaluator.Evaluate(Cards("8c 8d 8h 8s 2c Kc 3d"));
            Assert.That(kickerAce, Is.GreaterThan(kickerKing));
        }

        // ---- カテゴリ間の強弱 ----

        [Test]
        public void カテゴリの強さの順序が正しい()
        {
            var ordered = new[]
            {
                Eval("Ac Jd 9h 6s 3c"),  // ハイカード
                Eval("Tc Td Ah 7s 2c"),  // ワンペア
                Eval("Jc Jd 4h 4s Ac"),  // ツーペア
                Eval("Qc Qd Qh 8s 3c"),  // スリーカード
                Eval("9c 8d 7h 6s 5c"),  // ストレート
                Eval("Ah Jh 9h 6h 3h"),  // フラッシュ
                Eval("Kc Kd Kh 2c 2d"),  // フルハウス
                Eval("Ac Ad Ah As Kc"),  // フォーカード
                Eval("9h 8h 7h 6h 5h"),  // ストレートフラッシュ
            };
            for (int i = 1; i < ordered.Length; i++)
            {
                Assert.That(ordered[i], Is.GreaterThan(ordered[i - 1]),
                    $"{ordered[i].Category} は {ordered[i - 1].Category} より強いはず");
            }
        }

        // ---- 7枚からの最良5枚選択 ----

        [Test]
        public void 七枚からフラッシュを見つける()
        {
            var value = HandEvaluator.Evaluate(Cards("Ah Kh 9h 7h 3h Qs 2d"));
            Assert.That(value.Category, Is.EqualTo(HandCategory.Flush));
            Assert.That(value.Tiebreaks[0], Is.EqualTo((int)Rank.Ace));
        }

        [Test]
        public void 二つのスリーカードは強い方のフルハウスになる()
        {
            var value = HandEvaluator.Evaluate(Cards("Ah Ac As Kh Kc Ks 2d"));
            Assert.That(value.Category, Is.EqualTo(HandCategory.FullHouse));
            Assert.That(value.Tiebreaks[0], Is.EqualTo((int)Rank.Ace), "トリップスは A");
            Assert.That(value.Tiebreaks[1], Is.EqualTo((int)Rank.King), "ペアは K");
        }

        [Test]
        public void 六枚評価も動作する()
        {
            var value = HandEvaluator.Evaluate(Cards("9c 8d 7h 6s 5c 5d"));
            Assert.That(value.Category, Is.EqualTo(HandCategory.Straight));
        }

        // ---- 入力バリデーション ----

        [Test]
        public void 枚数不足は例外()
        {
            Assert.That(() => HandEvaluator.Evaluate(Cards("Ac Kd Qh Js")),
                Throws.ArgumentException);
        }

        [Test]
        public void 重複カードは例外()
        {
            Assert.That(() => HandEvaluator.Evaluate(Cards("Ac Ac Qh Js 9c")),
                Throws.ArgumentException);
        }
    }
}
