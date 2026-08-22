using System.Linq;
using KTC.Poker.Domain;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    public class HandEngineTests
    {
        private static Deck Rigged(string cards)
        {
            return new Deck(cards.Split(' ').Select(Card.Parse).ToArray());
        }

        /// <summary>
        /// 3人用 (btn=0) の11枚デッキ。配布順は s1,s2,s0 × 2周 → ボード5枚。
        /// s1: 2c,3c / s2: 7d,8d / s0: 9h,Th / ボード: 4s 5s Jc Qd Kh
        /// → seat0 が KQJT9 ストレートで勝つ。
        /// </summary>
        private const string DECK_3P = "2c 7d 9h 3c 8d Th 4s 5s Jc Qd Kh";

        private static HandEngine NewEngine3P(int stack = 200)
        {
            return new HandEngine(1, 2, new[] { stack, stack, stack }, 0, Rigged(DECK_3P));
        }

        private static void AssertChipConservation(HandEngine engine, int initialTotal)
        {
            int stacks = engine.Seats.Sum(s => s.Stack);
            if (engine.IsComplete)
            {
                Assert.That(stacks, Is.EqualTo(initialTotal), "終了後は全チップがスタックへ戻っているはず");
            }
            else
            {
                Assert.That(stacks + engine.Pot, Is.EqualTo(initialTotal), "進行中はスタック+ポットが一定のはず");
            }
        }

        // ---- セットアップ ----

        [Test]
        public void ブラインドと初手番_3人()
        {
            var engine = NewEngine3P();
            Assert.That(engine.SmallBlindIndex, Is.EqualTo(1));
            Assert.That(engine.BigBlindIndex, Is.EqualTo(2));
            Assert.That(engine.Seats[1].TotalCommitted, Is.EqualTo(1), "SB投入");
            Assert.That(engine.Seats[2].TotalCommitted, Is.EqualTo(2), "BB投入");
            Assert.That(engine.Pot, Is.EqualTo(3));
            Assert.That(engine.CurrentBet, Is.EqualTo(2));
            Assert.That(engine.MinRaiseTo, Is.EqualTo(4), "初期最小レイズは2BB");
            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(0), "プリフロップはBB左隣 (UTG=btn) から");
            Assert.That(engine.Seats[0].HoleCards[0], Is.EqualTo(Card.Parse("9h")));
            Assert.That(engine.Seats[0].HoleCards[1], Is.EqualTo(Card.Parse("Th")));
            AssertChipConservation(engine, 600);
        }

        [Test]
        public void ヘッズアップはボタンがSBで先に行動()
        {
            var engine = new HandEngine(1, 2, new[] { 100, 100 }, 0,
                Rigged("2c 7d 3c 8d 4s 5s Jc Qd Kh"));
            Assert.That(engine.SmallBlindIndex, Is.EqualTo(0), "HUはボタンがSB");
            Assert.That(engine.BigBlindIndex, Is.EqualTo(1));
            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(0), "HUプリフロップはボタンが先");
        }

        // ---- ベッティングラウンド進行 ----

        [Test]
        public void リンプ一周でBBオプション_チェックでフロップへ()
        {
            var engine = NewEngine3P();
            engine.Apply(PlayerAction.Call());  // seat0
            engine.Apply(PlayerAction.Call());  // seat1 (SB +1)
            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(2), "BBに手番が回る");
            var legal = engine.GetLegalActions();
            Assert.That(legal.CanCheck, Is.True, "BBオプション: チェック可");
            Assert.That(legal.CanRaise, Is.True, "BBオプション: レイズ可");
            engine.Apply(PlayerAction.Check());
            Assert.That(engine.CurrentStreet, Is.EqualTo(Street.Flop));
            Assert.That(engine.CommunityCards.Count, Is.EqualTo(3));
            Assert.That(engine.CurrentBet, Is.EqualTo(0), "新ストリートでベットはリセット");
            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(1), "ポストフロップはSBから");
            AssertChipConservation(engine, 600);
        }

        [Test]
        public void BBオプションのレイズでアクション再開()
        {
            var engine = NewEngine3P();
            engine.Apply(PlayerAction.Call());
            engine.Apply(PlayerAction.Call());
            engine.Apply(PlayerAction.RaiseTo(6)); // BBオプションレイズ
            Assert.That(engine.CurrentStreet, Is.EqualTo(Street.Preflop), "まだプリフロップ");
            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(0), "アクションが再開される");
            Assert.That(engine.CurrentBet, Is.EqualTo(6));
            Assert.That(engine.MinRaiseTo, Is.EqualTo(10));
        }

        [Test]
        public void 全員フォールドでBBが即勝利()
        {
            var engine = NewEngine3P();
            engine.Apply(PlayerAction.Fold()); // seat0
            engine.Apply(PlayerAction.Fold()); // seat1
            Assert.That(engine.IsComplete, Is.True);
            Assert.That(engine.Result.WentToShowdown, Is.False);
            Assert.That(engine.Result.Payouts[2], Is.EqualTo(3), "SB+BB=3を獲得");
            Assert.That(engine.Result.FinalStacks, Is.EqualTo(new[] { 200, 199, 201 }));
            Assert.That(engine.Result.ShowdownHands, Is.Empty, "フォールド決着では役は公開されない");
            AssertChipConservation(engine, 600);
        }

        [Test]
        public void チェックで全ストリート進行しショーダウン()
        {
            var engine = NewEngine3P();
            engine.Apply(PlayerAction.Call());
            engine.Apply(PlayerAction.Call());
            engine.Apply(PlayerAction.Check()); // → フロップ
            engine.Apply(PlayerAction.Check()); // seat1
            engine.Apply(PlayerAction.Check()); // seat2
            engine.Apply(PlayerAction.Check()); // seat0 → ターン
            Assert.That(engine.CurrentStreet, Is.EqualTo(Street.Turn));
            engine.Apply(PlayerAction.Check());
            engine.Apply(PlayerAction.Check());
            engine.Apply(PlayerAction.Check()); // → リバー
            Assert.That(engine.CurrentStreet, Is.EqualTo(Street.River));
            engine.Apply(PlayerAction.Check());
            engine.Apply(PlayerAction.Check());
            engine.Apply(PlayerAction.Check()); // → ショーダウン
            Assert.That(engine.IsComplete, Is.True);
            Assert.That(engine.Result.WentToShowdown, Is.True);
            Assert.That(engine.Result.ShowdownHands[0].Category, Is.EqualTo(HandCategory.Straight));
            Assert.That(engine.Result.Payouts[0], Is.EqualTo(6), "seat0がKハイストレートで総取り");
            Assert.That(engine.Result.FinalStacks[0], Is.EqualTo(204));
            AssertChipConservation(engine, 600);
        }

        // ---- ミニマムレイズ ----

        [Test]
        public void ミニマムレイズ未満は拒否()
        {
            var engine = NewEngine3P();
            Assert.That(() => engine.Apply(PlayerAction.RaiseTo(3)),
                Throws.InvalidOperationException, "最小レイズは4");
            engine.Apply(PlayerAction.RaiseTo(4)); // ちょうど最小はOK
            Assert.That(engine.CurrentBet, Is.EqualTo(4));
        }

        [Test]
        public void ミニマムレイズ幅は直前のフルレイズ幅で更新される()
        {
            var engine = NewEngine3P();
            engine.Apply(PlayerAction.RaiseTo(6));  // 幅4
            Assert.That(engine.MinRaiseTo, Is.EqualTo(10));
            engine.Apply(PlayerAction.RaiseTo(10)); // 幅4
            Assert.That(engine.MinRaiseTo, Is.EqualTo(14));
            engine.Apply(PlayerAction.RaiseTo(20)); // 幅10
            Assert.That(engine.MinRaiseTo, Is.EqualTo(30));
        }

        // ---- ショートオールイン (本物準拠ルールの核心) ----

        [Test]
        public void ショートオールインはアクションを再オープンしない()
        {
            // seat2 (BB) がスタック12でショートオールイン
            var deck = Rigged("2c 7d Ah 3c 8d Ad 4s 5s Jc Qd Kh"); // s0: Ah,Ad
            var engine = new HandEngine(1, 2, new[] { 100, 100, 12 }, 0, deck);
            engine.Apply(PlayerAction.RaiseTo(10)); // seat0
            engine.Apply(PlayerAction.Fold());      // seat1
            var bbLegal = engine.GetLegalActions();
            Assert.That(bbLegal.MinRaiseTo, Is.EqualTo(12), "オールインしかできないので12");
            engine.Apply(PlayerAction.RaiseTo(12)); // ショートオールイン (幅2 < 8)

            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(0), "seat0は差額のコール判断が必要");
            var legal = engine.GetLegalActions();
            Assert.That(legal.CanRaise, Is.False, "行動済みプレイヤーは再レイズ不可");
            Assert.That(legal.CanCall, Is.True);
            Assert.That(legal.CallAmount, Is.EqualTo(2));
            Assert.That(() => engine.Apply(PlayerAction.RaiseTo(20)),
                Throws.InvalidOperationException);

            engine.Apply(PlayerAction.Call());
            Assert.That(engine.IsComplete, Is.True, "残り1人が能動的 → 自動ランアウト");
            Assert.That(engine.CommunityCards.Count, Is.EqualTo(5));
            Assert.That(engine.Result.Payouts[0], Is.EqualTo(25), "AAがポット総取り");
            Assert.That(engine.Result.FinalStacks, Is.EqualTo(new[] { 113, 99, 0 }));
            AssertChipConservation(engine, 212);
        }

        [Test]
        public void ショートオールイン後も未行動プレイヤーはレイズ可能()
        {
            // seat1 (SB, スタック13) がショートオールイン、seat2 (BB) は未行動
            var deck = Rigged("As Kd 2h Ah Kc 3h 4c 5c 8s 9s Jd"); // s1: As,Ah / s2: Kd,Kc
            var engine = new HandEngine(1, 2, new[] { 200, 13, 200 }, 0, deck);
            engine.Apply(PlayerAction.RaiseTo(10)); // seat0
            engine.Apply(PlayerAction.RaiseTo(13)); // seat1 ショートオールイン (幅3 < 8)

            var bbLegal = engine.GetLegalActions();
            Assert.That(bbLegal.SeatIndex, Is.EqualTo(2));
            Assert.That(bbLegal.CanRaise, Is.True, "未行動プレイヤーのレイズ権は残る");
            Assert.That(bbLegal.MinRaiseTo, Is.EqualTo(21), "13 + 直前フルレイズ幅8");

            engine.Apply(PlayerAction.RaiseTo(21)); // フルレイズ (幅8) → アクション再オープン
            Assert.That(engine.CurrentSeatIndex, Is.EqualTo(0));
            Assert.That(engine.GetLegalActions().CanRaise, Is.True, "フルレイズを受けたので再レイズ可");

            engine.Apply(PlayerAction.Fold()); // seat0 降り → s1(オールイン) vs s2 でランアウト
            Assert.That(engine.IsComplete, Is.True);
            // メインポット36 (10+13+13) はAAのs1、サイド8はs2へ返る
            Assert.That(engine.Result.Payouts[1], Is.EqualTo(36));
            Assert.That(engine.Result.Payouts[2], Is.EqualTo(8));
            Assert.That(engine.Result.FinalStacks, Is.EqualTo(new[] { 190, 36, 187 }));
            AssertChipConservation(engine, 413);
        }

        // ---- サイドポット ----

        [Test]
        public void 三段のサイドポットが正しく分配される()
        {
            // スタック 10/50/100 が全員オールイン
            // s1: Ks,7d / s2: Qs,8d / s0: Ah,Ad / ボード: 2c 3c 4h 9s Th
            var deck = Rigged("Ks Qs Ah 7d 8d Ad 2c 3c 4h 9s Th");
            var engine = new HandEngine(1, 2, new[] { 10, 50, 100 }, 0, deck);
            engine.Apply(PlayerAction.RaiseTo(10));  // seat0 オールイン (フルレイズ幅8)
            engine.Apply(PlayerAction.RaiseTo(50));  // seat1 オールイン
            engine.Apply(PlayerAction.RaiseTo(100)); // seat2 オールイン

            Assert.That(engine.IsComplete, Is.True, "全員オールイン → 自動ランアウト");
            Assert.That(engine.Result.Pots.Count, Is.EqualTo(3));
            Assert.That(engine.Result.Pots[0].Amount, Is.EqualTo(30), "メインポット 10×3");
            Assert.That(engine.Result.Pots[0].WinnerSeats, Is.EqualTo(new[] { 0 }), "AAがメイン獲得");
            Assert.That(engine.Result.Pots[1].Amount, Is.EqualTo(80), "サイド1 40×2");
            Assert.That(engine.Result.Pots[1].WinnerSeats, Is.EqualTo(new[] { 1 }), "KハイがQハイに勝つ");
            Assert.That(engine.Result.Pots[2].Amount, Is.EqualTo(50), "サイド2は単独返還");
            Assert.That(engine.Result.Pots[2].WinnerSeats, Is.EqualTo(new[] { 2 }));
            Assert.That(engine.Result.FinalStacks, Is.EqualTo(new[] { 30, 80, 50 }));
            AssertChipConservation(engine, 160);
        }

        [Test]
        public void スプリットポットの端数はボタン左隣に近い順()
        {
            // s0とs2がボードのロイヤルフラッシュで引き分け。SBの死に金1でポットは奇数21
            var deck = Rigged("2c 4c 3d 2d 4d 3h As Ks Qs Js Ts");
            var engine = new HandEngine(1, 2, new[] { 10, 10, 10 }, 0, deck);
            engine.Apply(PlayerAction.RaiseTo(10)); // seat0 オールイン
            engine.Apply(PlayerAction.Fold());      // seat1 (SB 1 が死に金)
            engine.Apply(PlayerAction.Call());      // seat2 オールイン

            Assert.That(engine.IsComplete, Is.True);
            Assert.That(engine.Pot, Is.EqualTo(21));
            Assert.That(engine.Result.Payouts[2], Is.EqualTo(11), "ボタン左隣に近いseat2が端数+1");
            Assert.That(engine.Result.Payouts[0], Is.EqualTo(10));
            AssertChipConservation(engine, 30);
        }

        // ---- 不正アクション ----

        [Test]
        public void 不正アクションは拒否される()
        {
            var engine = NewEngine3P();
            Assert.That(() => engine.Apply(PlayerAction.Check()),
                Throws.InvalidOperationException, "BBに対してチェックは不可");

            engine.Apply(PlayerAction.Call());
            engine.Apply(PlayerAction.Call());
            Assert.That(() => engine.Apply(PlayerAction.Call()),
                Throws.InvalidOperationException, "コールする額がない (チェックすべき)");

            engine.Apply(PlayerAction.Check());
            Assert.That(engine.CurrentStreet, Is.EqualTo(Street.Flop));
        }

        [Test]
        public void 終了後のアクションは拒否される()
        {
            var engine = NewEngine3P();
            engine.Apply(PlayerAction.Fold());
            engine.Apply(PlayerAction.Fold());
            Assert.That(engine.IsComplete, Is.True);
            Assert.That(() => engine.GetLegalActions(), Throws.InvalidOperationException);
            Assert.That(() => engine.Apply(PlayerAction.Fold()), Throws.InvalidOperationException);
        }
    }
}
