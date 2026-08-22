using System;
using System.Collections.Generic;

namespace KTC.Poker.Domain
{
    /// <summary>
    /// テキサスホールデムの役判定。5〜7枚から最強の5枚役を求める。
    /// 実装は「全ての5枚組合せを評価して最大を取る」方式 (7枚なら21通り)。
    /// 高速化した実装よりも教材として追いやすいことを優先している。
    /// </summary>
    public static class HandEvaluator
    {
        /// <summary>5〜7枚のカードから最強の5枚役を評価する。</summary>
        public static HandValue Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }
            int count = cards.Count;
            if (count < 5 || count > 7)
            {
                throw new ArgumentException($"5〜7枚のカードが必要です (受領: {count}枚)。", nameof(cards));
            }
            // 未公開カードと重複カードはデッキ管理のバグなので早期に検出する
            for (int i = 0; i < count; i++)
            {
                if (cards[i].IsNone)
                {
                    throw new ArgumentException("未公開カード (None) は評価できません。", nameof(cards));
                }
                for (int j = i + 1; j < count; j++)
                {
                    if (cards[i] == cards[j])
                    {
                        throw new ArgumentException($"カードが重複しています: {cards[i]}", nameof(cards));
                    }
                }
            }

            if (count == 5)
            {
                return EvaluateFive(cards[0], cards[1], cards[2], cards[3], cards[4]);
            }

            // 6枚: 1枚除外 (6通り) / 7枚: 2枚除外 (21通り) の全組合せで最大を探す
            Card[] buffer = new Card[5];
            HandValue best = default; // score 0 = どの実役よりも弱い番兵
            for (int skipA = 0; skipA < count; skipA++)
            {
                if (count == 6)
                {
                    FillExcluding(cards, buffer, skipA, -1);
                    HandValue value = EvaluateFive(buffer[0], buffer[1], buffer[2], buffer[3], buffer[4]);
                    if (value > best) best = value;
                }
                else
                {
                    for (int skipB = skipA + 1; skipB < count; skipB++)
                    {
                        FillExcluding(cards, buffer, skipA, skipB);
                        HandValue value = EvaluateFive(buffer[0], buffer[1], buffer[2], buffer[3], buffer[4]);
                        if (value > best) best = value;
                    }
                }
            }
            return best;
        }

        private static void FillExcluding(IReadOnlyList<Card> source, Card[] buffer, int skipA, int skipB)
        {
            int write = 0;
            for (int i = 0; i < source.Count; i++)
            {
                if (i == skipA || i == skipB) continue;
                buffer[write++] = source[i];
            }
        }

        private static HandValue EvaluateFive(Card c1, Card c2, Card c3, Card c4, Card c5)
        {
            // ランク出現数 (index 2..14)
            int[] rankCount = new int[15];
            rankCount[(int)c1.Rank]++;
            rankCount[(int)c2.Rank]++;
            rankCount[(int)c3.Rank]++;
            rankCount[(int)c4.Rank]++;
            rankCount[(int)c5.Rank]++;

            bool isFlush = c1.Suit == c2.Suit && c2.Suit == c3.Suit && c3.Suit == c4.Suit && c4.Suit == c5.Suit;

            // 出現数ごとのランクを強い順に収集
            int quad = 0, trips = 0, pairHigh = 0, pairLow = 0;
            List<int> singles = new List<int>(5); // キッカー (強い順)
            for (int r = 14; r >= 2; r--)
            {
                switch (rankCount[r])
                {
                    case 4: quad = r; break;
                    case 3: trips = r; break;
                    case 2:
                        if (pairHigh == 0) pairHigh = r;
                        else pairLow = r;
                        break;
                    case 1: singles.Add(r); break;
                }
            }

            // ストレート判定 (5枚全て異なるランクの場合のみ成立し得る)
            int straightHigh = 0;
            if (singles.Count == 5)
            {
                if (singles[0] - singles[4] == 4)
                {
                    straightHigh = singles[0];
                }
                else if (singles[0] == 14 && singles[1] == 5 && singles[4] == 2)
                {
                    straightHigh = 5; // ホイール (A-2-3-4-5) は 5 ハイ
                }
            }

            if (straightHigh > 0 && isFlush)
            {
                return HandValue.Create(HandCategory.StraightFlush, straightHigh);
            }
            if (quad > 0)
            {
                return HandValue.Create(HandCategory.FourOfAKind, quad, singles[0]);
            }
            if (trips > 0 && pairHigh > 0)
            {
                return HandValue.Create(HandCategory.FullHouse, trips, pairHigh);
            }
            if (isFlush)
            {
                return HandValue.Create(HandCategory.Flush, singles[0], singles[1], singles[2], singles[3], singles[4]);
            }
            if (straightHigh > 0)
            {
                return HandValue.Create(HandCategory.Straight, straightHigh);
            }
            if (trips > 0)
            {
                return HandValue.Create(HandCategory.ThreeOfAKind, trips, singles[0], singles[1]);
            }
            if (pairLow > 0)
            {
                return HandValue.Create(HandCategory.TwoPair, pairHigh, pairLow, singles[0]);
            }
            if (pairHigh > 0)
            {
                return HandValue.Create(HandCategory.OnePair, pairHigh, singles[0], singles[1], singles[2]);
            }
            return HandValue.Create(HandCategory.HighCard, singles[0], singles[1], singles[2], singles[3], singles[4]);
        }
    }
}
