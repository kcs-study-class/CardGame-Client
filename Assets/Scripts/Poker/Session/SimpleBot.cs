using System;
using System.Collections.Generic;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;

namespace KTC.Poker.Session
{
    /// <summary>
    /// ハンド強度ベースの Bot。CallingBot より人間らしい振る舞いをする:
    /// - 強いハンドはレイズ (確率的)、無料ならチェック、弱いハンドは高いコールを降りる
    /// - 判定は自分視点のリダクション済みスナップショットのみ (カンニング不可は CallingBot と同じ)
    /// 生徒の拡張課題の出発点: EstimateStrength を賢くする、ポットオッズを見る、ブラフを混ぜる等。
    /// </summary>
    public sealed class SimpleBot : IBotPolicy
    {
        private readonly Random _random;

        /// <param name="seed">0 以外を渡すと決定論的になる (テスト・デバッグ用)。</param>
        public SimpleBot(int seed = 0)
        {
            _random = seed == 0 ? new Random() : new Random(seed);
        }

        public PlayerActionMessage Decide(TableStateMessage view, ActionRequestMessage legal)
        {
            float strength = EstimateStrength(view) + (float)(_random.NextDouble() * 0.2 - 0.1);

            if (legal.canRaise && strength > 0.7f && _random.NextDouble() < 0.6)
            {
                return new PlayerActionMessage { actionType = (int)ActionType.RaiseTo, amount = legal.minRaiseTo };
            }
            if (legal.canCheck)
            {
                // 無料で回ってくるなら降りる理由がない
                return new PlayerActionMessage { actionType = (int)ActionType.Check };
            }

            // コールにチップが必要な局面: 弱いハンド・高すぎるコールは降りる
            var me = FindSeat(view);
            bool expensive = me != null && legal.callAmount * 20 > Math.Max(1, me.stack) * 3; // スタックの15%超
            if (strength < 0.35f || (strength < 0.5f && expensive))
            {
                return new PlayerActionMessage { actionType = (int)ActionType.Fold };
            }
            return new PlayerActionMessage { actionType = (int)ActionType.Call };
        }

        /// <summary>現時点のハンド強度を 0..1 で概算する (プリフロップはヒューリスティック、フロップ以降は役評価)。</summary>
        private static float EstimateStrength(TableStateMessage view)
        {
            var me = FindSeat(view);
            if (me == null || me.holeCards == null || me.holeCards.Length < 2)
            {
                return 0.3f;
            }
            if (!Card.TryFromValue(me.holeCards[0], out var c1) || !Card.TryFromValue(me.holeCards[1], out var c2))
            {
                return 0.3f;
            }

            if (view.communityCards == null || view.communityCards.Length == 0)
            {
                int r1 = (int)c1.Rank, r2 = (int)c2.Rank;
                if (r1 == r2)
                {
                    return 0.6f + r1 / 50f; // 22=0.64 .. AA=0.88
                }
                int high = Math.Max(r1, r2), low = Math.Min(r1, r2);
                bool suited = c1.Suit == c2.Suit;
                float value = 0.15f + high / 40f + low / 80f + (suited ? 0.05f : 0f);
                return Math.Min(value, 0.62f);
            }

            var cards = new List<Card>(7) { c1, c2 };
            foreach (byte packed in view.communityCards)
            {
                if (Card.TryFromValue(packed, out var card))
                {
                    cards.Add(card);
                }
            }
            return HandEvaluator.Evaluate(cards).Category switch
            {
                HandCategory.HighCard => 0.2f,
                HandCategory.OnePair => 0.45f,
                HandCategory.TwoPair => 0.65f,
                HandCategory.ThreeOfAKind => 0.78f,
                HandCategory.Straight => 0.85f,
                HandCategory.Flush => 0.88f,
                _ => 0.95f, // フルハウス以上
            };
        }

        private static SeatStateMessage FindSeat(TableStateMessage view)
        {
            foreach (var seat in view.seats)
            {
                if (seat.seat == view.yourSeat)
                {
                    return seat;
                }
            }
            return null;
        }
    }
}
