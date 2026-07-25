using System.Collections.Generic;
using System.Linq;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using KTC.Poker.Session;
using NUnit.Framework;

namespace KTC.Poker.Tests
{
    public class LocalGameSessionTests
    {
        private static Deck Rigged(string cards)
        {
            return new Deck(cards.Split(' ').Select(Card.Parse).ToArray());
        }

        /// <summary>
        /// 4人卓 (btn=seat0, 自分=seat0) 用デッキ。配布順は table 1,2,3,0 × 2周。
        /// s1: 2c,2d / s2: 4c,4d / s3: 6c,6d / s0(自分): 8c,8d
        /// ボード: Ts Js Qs Ks 9h → 全員ボードのストレートで4分割。
        /// </summary>
        private const string Deck4P = "2c 4c 6c 8c 2d 4d 6d 8d Ts Js Qs Ks 9h";

        private sealed class Recorder
        {
            public readonly List<TableStateMessage> States = new List<TableStateMessage>();
            public readonly List<string> Errors = new List<string>();
            public TableStateMessage Last => States[States.Count - 1];

            public void Attach(IGameSession session)
            {
                session.StateUpdated += s => States.Add(s);
                session.ErrorOccurred += e => Errors.Add(e);
            }
        }

        private static LocalGameSession NewSession4P(out Recorder recorder, params string[] decks)
        {
            var session = new LocalGameSession(new LocalGameSessionConfig
            {
                SeatCount = 4,
                StartingStack = 200,
                SmallBlind = 1,
                BigBlind = 2,
                MySeat = 0,
            });
            var queue = new Queue<Deck>(decks.Select(Rigged));
            if (queue.Count > 0)
            {
                session.DeckFactory = () => queue.Dequeue();
            }
            recorder = new Recorder();
            recorder.Attach(session);
            return session;
        }

        private static PlayerActionMessage Msg(ActionType type, int amount = 0)
        {
            return new PlayerActionMessage { actionType = (int)type, amount = amount };
        }

        // ---- 基本フロー ----

        [Test]
        public void 接続で初期状態が届き自分の手番まで進む()
        {
            var session = NewSession4P(out var recorder, Deck4P);
            session.Connect();

            Assert.That(session.IsConnected, Is.True);
            Assert.That(recorder.States, Is.Not.Empty);
            var last = recorder.Last;
            Assert.That(last.handNumber, Is.EqualTo(1));
            Assert.That(last.isYourTurn, Is.True, "UTGのBot(s3)が行動後、自分(s0)の手番で止まる");
            Assert.That(last.currentSeat, Is.EqualTo(0));
            Assert.That(last.buttonSeat, Is.EqualTo(0));
            Assert.That(last.smallBlindSeat, Is.EqualTo(1));
            Assert.That(last.bigBlindSeat, Is.EqualTo(2));
            Assert.That(last.actionRequest.canCall, Is.True);
            Assert.That(last.actionRequest.callAmount, Is.EqualTo(2));
            Assert.That(last.pot, Is.EqualTo(5), "SB1+BB2+s3コール2");
        }

        [Test]
        public void 他人のホールカードは伏せられ自分のは見える()
        {
            var session = NewSession4P(out var recorder, Deck4P);
            session.Connect();

            foreach (var state in recorder.States)
            {
                var mySeat = state.seats[0];
                Assert.That(mySeat.holeCards, Is.EqualTo(new[]
                {
                    Card.Parse("8c").Value, Card.Parse("8d").Value,
                }), "自分の手札は実値");
                for (int seat = 1; seat < 4; seat++)
                {
                    Assert.That(state.seats[seat].holeCards, Is.All.EqualTo((byte)0),
                        $"seat{seat} の手札は伏せる");
                }
            }
        }

        [Test]
        public void チェックコールで完走しショーダウンで公開される()
        {
            var session = NewSession4P(out var recorder, Deck4P);
            session.Connect();

            session.SendAction(Msg(ActionType.Call));   // プリフロップ
            session.SendAction(Msg(ActionType.Check));  // フロップ
            session.SendAction(Msg(ActionType.Check));  // ターン
            session.SendAction(Msg(ActionType.Check));  // リバー

            var last = recorder.Last;
            Assert.That(recorder.Errors, Is.Empty);
            Assert.That(last.isComplete, Is.True);
            Assert.That(last.result.wentToShowdown, Is.True);
            Assert.That(last.result.payouts.Sum(), Is.EqualTo(8), "ポット総額が分配される");
            Assert.That(last.result.showdownHands.Length, Is.EqualTo(4));
            Assert.That(last.seats.Sum(s => s.stack), Is.EqualTo(800), "チップ保存則");
            foreach (var seat in last.seats)
            {
                Assert.That(seat.holeCards, Is.All.Not.EqualTo((byte)0),
                    "ショーダウン参加者の手札は公開される");
            }
            // 全員ボードのストレート
            Assert.That(last.result.showdownHands.Select(h => h.category),
                Is.All.EqualTo((int)HandCategory.Straight));
        }

        [Test]
        public void フォールドするとBotだけで決着する()
        {
            var session = NewSession4P(out var recorder, Deck4P);
            session.Connect();
            session.SendAction(Msg(ActionType.Fold));

            var last = recorder.Last;
            Assert.That(last.isComplete, Is.True);
            Assert.That(last.result.payouts[0], Is.EqualTo(0), "降りた自分の獲得は0");
            Assert.That(last.result.payouts.Sum(), Is.GreaterThan(0));
            Assert.That(last.seats[0].folded, Is.True);
            Assert.That(last.seats[0].holeCards, Is.All.Not.EqualTo((byte)0),
                "自分の手札は降りても自分には見える");
        }

        // ---- 次ハンド進行 ----

        [Test]
        public void SendReadyで次ハンドへ進みボタンが回る()
        {
            var session = NewSession4P(out var recorder, Deck4P, Deck4P);
            session.Connect();
            session.SendAction(Msg(ActionType.Call));
            session.SendAction(Msg(ActionType.Check));
            session.SendAction(Msg(ActionType.Check));
            session.SendAction(Msg(ActionType.Check));
            Assert.That(recorder.Last.isComplete, Is.True);

            session.SendReady();

            var last = recorder.Last;
            Assert.That(recorder.Errors, Is.Empty);
            Assert.That(last.handNumber, Is.EqualTo(2));
            Assert.That(last.isComplete, Is.False);
            Assert.That(last.buttonSeat, Is.EqualTo(1), "ボタンは次の席へ");
            Assert.That(last.smallBlindSeat, Is.EqualTo(2));
            Assert.That(last.bigBlindSeat, Is.EqualTo(3));
        }

        // ---- エラー処理 ----

        [Test]
        public void 不正な操作はErrorOccurredで通知され続行できる()
        {
            var session = NewSession4P(out var recorder, Deck4P);
            session.Connect();

            session.SendAction(Msg(ActionType.RaiseTo, 3)); // 最小レイズ4未満
            Assert.That(recorder.Errors, Has.Count.EqualTo(1), "最小レイズ違反が通知される");

            session.SendAction(Msg((ActionType)99));
            Assert.That(recorder.Errors, Has.Count.EqualTo(2), "未知のactionTypeが通知される");

            session.SendReady();
            Assert.That(recorder.Errors, Has.Count.EqualTo(3), "進行中のSendReadyが通知される");

            session.SendAction(Msg(ActionType.Call)); // 正常アクションは通る
            Assert.That(recorder.Errors, Has.Count.EqualTo(3));
            Assert.That(recorder.Last.street, Is.EqualTo((int)Street.Flop).Or.EqualTo((int)Street.Preflop));
        }

        [Test]
        public void ハンド終了後のアクションは拒否される()
        {
            var session = NewSession4P(out var recorder, Deck4P);
            session.Connect();
            session.SendAction(Msg(ActionType.Fold));
            Assert.That(recorder.Last.isComplete, Is.True);

            int before = recorder.Errors.Count;
            session.SendAction(Msg(ActionType.Check));
            Assert.That(recorder.Errors, Has.Count.EqualTo(before + 1));
        }

        // ---- デバッグ設定 ----

        [Test]
        public void RevealAllHoleCardsで全手札が公開される()
        {
            var session = new LocalGameSession(new LocalGameSessionConfig
            {
                SeatCount = 4,
                MySeat = 0,
                RevealAllHoleCards = true,
                DeckFactory = () => Rigged(Deck4P),
            });
            var recorder = new Recorder();
            recorder.Attach(session);
            session.Connect();

            foreach (var seat in recorder.Last.seats)
            {
                Assert.That(seat.holeCards, Is.All.Not.EqualTo((byte)0),
                    $"seat{seat.seat} の手札が公開されている");
            }
        }

        [Test]
        public void ConfigのDeckFactoryで積み込みできる()
        {
            var session = new LocalGameSession(new LocalGameSessionConfig
            {
                SeatCount = 4,
                MySeat = 0,
                DeckFactory = () => Rigged(Deck4P),
            });
            var recorder = new Recorder();
            recorder.Attach(session);
            session.Connect();

            Assert.That(recorder.Last.seats[0].holeCards, Is.EqualTo(new[]
            {
                Card.Parse("8c").Value, Card.Parse("8d").Value,
            }), "config経由の積み込みデッキが使われる");
        }

        // ---- バスト・ゲームオーバー ----

        [Test]
        public void 相手をバストさせるとゲームオーバー()
        {
            // HU: 自分(s0)=AA, Bot(s1)=KK。全額入れて自分が勝つ。
            // HU配布順: s1,s0,s1,s0 / ボードにフラッシュ・ストレートなし
            var session = new LocalGameSession(new LocalGameSessionConfig
            {
                SeatCount = 2,
                SmallBlind = 1,
                BigBlind = 2,
                MySeat = 0,
                InitialStacks = new[] { 6, 6 },
            });
            session.DeckFactory = () => Rigged("Kh Ah Kd Ad 2c 7d 8s 9s 2h");
            var recorder = new Recorder();
            recorder.Attach(session);

            session.Connect();
            Assert.That(recorder.Last.isYourTurn, Is.True, "HUプリフロップはボタン(自分)が先");

            session.SendAction(Msg(ActionType.RaiseTo, 6)); // 全額 → Botがコール → ランアウト
            var afterHand = recorder.Last;
            Assert.That(afterHand.isComplete, Is.True);
            Assert.That(afterHand.result.payouts[0], Is.EqualTo(12), "AAが総取り");
            Assert.That(afterHand.seats[1].stack, Is.EqualTo(0));

            session.SendReady();
            var final = recorder.Last;
            Assert.That(final.isGameOver, Is.True, "参加可能な席が1つ → 卓終了");
            Assert.That(final.seats[0].stack, Is.EqualTo(12));
            Assert.That(final.seats[1].stack, Is.EqualTo(0));
        }
    }
}
