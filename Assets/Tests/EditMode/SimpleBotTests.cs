using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using KTC.Poker.Session;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    public class SimpleBotTests
    {
        private static TableStateMessage MakeView(byte hole1, byte hole2, byte[] community = null)
        {
            return new TableStateMessage
            {
                yourSeat = 0,
                communityCards = community ?? new byte[0],
                seats = new[]
                {
                    new SeatStateMessage { seat = 0, stack = 200, holeCards = new[] { hole1, hole2 } },
                    new SeatStateMessage { seat = 1, stack = 200, holeCards = new byte[] { 0, 0 } },
                },
            };
        }

        private static byte Packed(Rank rank, Suit suit) => new Card(rank, suit).Value;

        [Test]
        public void 無料でチェックできるなら絶対に降りない()
        {
            var bot = new SimpleBot(seed: 1);
            // 最弱クラスのハンド (72o)
            var view = MakeView(Packed(Rank.Seven, Suit.Spade), Packed(Rank.Two, Suit.Heart));
            var legal = new ActionRequestMessage { canCheck = true, canRaise = true, minRaiseTo = 4, maxRaiseTo = 200 };

            for (int i = 0; i < 200; i++)
            {
                var action = bot.Decide(view, legal);
                Assert.AreNotEqual((int)ActionType.Fold, action.actionType, "チェック可能な局面でフォールドした");
            }
        }

        [Test]
        public void 強いハンドはレイズすることがあり額は常にminRaiseTo()
        {
            var bot = new SimpleBot(seed: 2);
            // AA
            var view = MakeView(Packed(Rank.Ace, Suit.Spade), Packed(Rank.Ace, Suit.Heart));
            var legal = new ActionRequestMessage { canCheck = false, canCall = true, callAmount = 2, canRaise = true, minRaiseTo = 4, maxRaiseTo = 200 };

            int raises = 0;
            for (int i = 0; i < 200; i++)
            {
                var action = bot.Decide(view, legal);
                if (action.actionType == (int)ActionType.RaiseTo)
                {
                    raises++;
                    Assert.AreEqual(4, action.amount, "レイズ額が minRaiseTo と一致しない");
                }
            }
            Assert.Greater(raises, 0, "AA で一度もレイズしなかった");
        }

        [Test]
        public void 弱いハンドは高額コールを降りることがある()
        {
            var bot = new SimpleBot(seed: 3);
            // 72o に対しスタックの半分のコール要求
            var view = MakeView(Packed(Rank.Seven, Suit.Spade), Packed(Rank.Two, Suit.Heart));
            var legal = new ActionRequestMessage { canCheck = false, canCall = true, callAmount = 100, canRaise = false, minRaiseTo = 0, maxRaiseTo = 0 };

            int folds = 0;
            for (int i = 0; i < 200; i++)
            {
                if (bot.Decide(view, legal).actionType == (int)ActionType.Fold)
                {
                    folds++;
                }
            }
            Assert.Greater(folds, 0, "弱いハンドの高額コールを一度も降りなかった");
        }

        [Test]
        public void シード指定で決定論的になる()
        {
            var view = MakeView(Packed(Rank.King, Suit.Spade), Packed(Rank.Queen, Suit.Spade));
            var legal = new ActionRequestMessage { canCheck = false, canCall = true, callAmount = 2, canRaise = true, minRaiseTo = 4, maxRaiseTo = 200 };

            var botA = new SimpleBot(seed: 7);
            var botB = new SimpleBot(seed: 7);
            for (int i = 0; i < 50; i++)
            {
                var a = botA.Decide(view, legal);
                var b = botB.Decide(view, legal);
                Assert.AreEqual(a.actionType, b.actionType);
                Assert.AreEqual(a.amount, b.amount);
            }
        }

        [Test]
        public void フロップ以降は役評価で強くなる_ツーペアでコールする()
        {
            var bot = new SimpleBot(seed: 4);
            // A7 / ボード A-7-K → ツーペア
            var view = MakeView(
                Packed(Rank.Ace, Suit.Spade), Packed(Rank.Seven, Suit.Heart),
                new[] { Packed(Rank.Ace, Suit.Diamond), Packed(Rank.Seven, Suit.Club), Packed(Rank.King, Suit.Spade) });
            var legal = new ActionRequestMessage { canCheck = false, canCall = true, callAmount = 10, canRaise = false };

            for (int i = 0; i < 200; i++)
            {
                Assert.AreNotEqual((int)ActionType.Fold, bot.Decide(view, legal).actionType, "ツーペアで降りた");
            }
        }
    }
}
