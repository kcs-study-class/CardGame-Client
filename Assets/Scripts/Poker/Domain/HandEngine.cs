using System;
using System.Collections.Generic;
using System.Linq;

namespace KTC.Poker.Domain
{
    /// <summary>
    /// テキサスホールデム1ハンド分の状態機械。サーバーオーソリタティブ設計の「サーバー側」に相当し、
    /// UI からはアクションを <see cref="Apply"/> するだけで、状態変更はすべてエンジン内で完結する。
    ///
    /// ルールは本物のノーリミットホールデム準拠:
    /// - 最小レイズ幅 = 直前のフルレイズ幅 (初期値はBB)
    /// - 最小レイズ幅未満のオールイン (ショートオールイン) は許可されるが、
    ///   アクションを「再オープンしない」= 既に行動済みのプレイヤーはコール/フォールドのみ
    /// - サイドポットは投入額の階層で自動計算
    ///
    /// 配布順: ボタンの左隣から時計回りに1枚ずつ2周 → フロップ3枚 → ターン → リバー (バーンカードなし)。
    /// </summary>
    public sealed class HandEngine
    {
        public int SmallBlind { get; }
        public int BigBlind { get; }
        public int ButtonIndex { get; }
        public int SmallBlindIndex { get; }
        public int BigBlindIndex { get; }

        private readonly Deck _deck;
        private readonly List<SeatState> _seats;
        private readonly List<Card> _community = new List<Card>(5);

        public Street CurrentStreet { get; private set; } = Street.Preflop;

        /// <summary>現在ストリートの最高ベット額 (StreetBet 基準)。</summary>
        public int CurrentBet { get; private set; }

        /// <summary>直前のフルレイズ幅。ショートオールインでは更新されない。</summary>
        private int _lastRaiseSize;

        /// <summary>手番の座席。ハンド完了時は -1。</summary>
        public int CurrentSeatIndex { get; private set; } = -1;

        public bool IsComplete { get; private set; }
        public HandResult Result { get; private set; }

        public IReadOnlyList<SeatState> Seats => _seats;
        public IReadOnlyList<Card> CommunityCards => _community;

        /// <summary>最小レイズ後の合計ベット額。</summary>
        public int MinRaiseTo => CurrentBet + _lastRaiseSize;

        /// <summary>全座席の投入額合計 (現在のポット)。</summary>
        public int Pot
        {
            get
            {
                int sum = 0;
                foreach (var seat in _seats)
                {
                    sum += seat.TotalCommitted;
                }
                return sum;
            }
        }

        public HandEngine(int smallBlind, int bigBlind, IReadOnlyList<int> stacks, int buttonIndex, Deck deck)
        {
            if (smallBlind <= 0 || bigBlind < smallBlind)
            {
                throw new ArgumentException($"ブラインドが不正です (SB={smallBlind}, BB={bigBlind})。");
            }
            if (stacks == null || stacks.Count < 2 || stacks.Count > 9)
            {
                throw new ArgumentException("プレイヤー数は 2〜9 人にしてください。", nameof(stacks));
            }
            if (buttonIndex < 0 || buttonIndex >= stacks.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(buttonIndex));
            }
            _deck = deck ?? throw new ArgumentNullException(nameof(deck));
            if (deck.Remaining < stacks.Count * 2 + 5)
            {
                throw new ArgumentException("デッキの残り枚数が不足しています。", nameof(deck));
            }

            _seats = new List<SeatState>(stacks.Count);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i] <= 0)
                {
                    throw new ArgumentException($"座席 {i} のスタックが 0 以下です。参加者のみ渡してください。", nameof(stacks));
                }
                _seats.Add(new SeatState(i, stacks[i]));
            }

            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            ButtonIndex = buttonIndex;

            // ヘッズアップはボタンが SB
            if (_seats.Count == 2)
            {
                SmallBlindIndex = buttonIndex;
                BigBlindIndex = NextSeat(buttonIndex);
            }
            else
            {
                SmallBlindIndex = NextSeat(buttonIndex);
                BigBlindIndex = NextSeat(SmallBlindIndex);
            }

            DealHoleCards();
            PostBlind(SmallBlindIndex, SmallBlind);
            PostBlind(BigBlindIndex, BigBlind);
            CurrentBet = BigBlind;
            _lastRaiseSize = BigBlind;

            if (CountActiveNonAllIn() >= 2)
            {
                CurrentSeatIndex = FindNextActor(BigBlindIndex);
            }
            else
            {
                // ブラインドの時点で全員オールイン → 自動ランアウト
                AdvanceStreetOrShowdown();
            }
        }

        // ---- 公開 API ----

        /// <summary>現在の手番プレイヤーが取れるアクションを返す。</summary>
        public LegalActions GetLegalActions()
        {
            if (IsComplete || CurrentSeatIndex < 0)
            {
                throw new InvalidOperationException("ハンドは終了しています。");
            }
            var seat = _seats[CurrentSeatIndex];
            int toCall = CurrentBet - seat.StreetBet;
            int maxRaiseTo = seat.StreetBet + seat.Stack;
            // フルレイズ以降未行動なら (=HasActed が false なら) レイズ権がある。
            // ショートオールインは HasActed をリセットしないため、行動済みプレイヤーの再レイズは自然に禁止される。
            bool canRaise = !seat.HasActed && maxRaiseTo > CurrentBet;
            return new LegalActions
            {
                SeatIndex = CurrentSeatIndex,
                CanFold = true,
                CanCheck = toCall == 0,
                CanCall = toCall > 0,
                CallAmount = Math.Min(toCall, seat.Stack),
                CanRaise = canRaise,
                MinRaiseTo = canRaise ? Math.Min(MinRaiseTo, maxRaiseTo) : 0,
                MaxRaiseTo = canRaise ? maxRaiseTo : 0,
            };
        }

        /// <summary>現在の手番プレイヤーのアクションを適用し、ハンドを進行させる。</summary>
        public void Apply(PlayerAction action)
        {
            var legal = GetLegalActions();
            var seat = _seats[CurrentSeatIndex];

            switch (action.Type)
            {
                case ActionType.Fold:
                    seat.HasFolded = true;
                    break;

                case ActionType.Check:
                    if (!legal.CanCheck)
                    {
                        throw new InvalidOperationException($"座席 {seat.SeatIndex} はチェックできません (要コール額: {legal.CallAmount})。");
                    }
                    break;

                case ActionType.Call:
                    if (!legal.CanCall)
                    {
                        throw new InvalidOperationException($"座席 {seat.SeatIndex} にコールする額がありません。チェックしてください。");
                    }
                    Commit(seat, legal.CallAmount);
                    break;

                case ActionType.RaiseTo:
                    ApplyRaise(seat, legal, action.Amount);
                    break;

                default:
                    throw new InvalidOperationException($"未知のアクションです: {action.Type}");
            }

            seat.HasActed = true;
            AfterAction();
        }

        // ---- 内部処理 ----

        private void ApplyRaise(SeatState seat, LegalActions legal, int raiseTo)
        {
            if (!legal.CanRaise)
            {
                throw new InvalidOperationException($"座席 {seat.SeatIndex} は現在レイズできません (ショートオールイン後の再レイズ等)。");
            }
            if (raiseTo <= CurrentBet)
            {
                throw new InvalidOperationException($"レイズ額 {raiseTo} は現在のベット {CurrentBet} を超えている必要があります。");
            }
            if (raiseTo > legal.MaxRaiseTo)
            {
                throw new InvalidOperationException($"レイズ額 {raiseTo} がスタック上限 {legal.MaxRaiseTo} を超えています。");
            }
            bool isAllIn = raiseTo == legal.MaxRaiseTo;
            if (raiseTo < MinRaiseTo && !isAllIn)
            {
                throw new InvalidOperationException($"最小レイズ額は {MinRaiseTo} です (オールインを除く)。");
            }

            int raiseSize = raiseTo - CurrentBet;
            Commit(seat, raiseTo - seat.StreetBet);
            CurrentBet = raiseTo;

            // フルレイズならアクションを再オープンする (全員に再行動権)
            if (raiseSize >= _lastRaiseSize)
            {
                _lastRaiseSize = raiseSize;
                foreach (var other in _seats)
                {
                    if (other != seat && !other.HasFolded && !other.IsAllIn)
                    {
                        other.HasActed = false;
                    }
                }
            }
        }

        private void Commit(SeatState seat, int amount)
        {
            if (amount < 0 || amount > seat.Stack)
            {
                throw new InvalidOperationException($"不正なチップ移動です: {amount} (stack: {seat.Stack})");
            }
            seat.Stack -= amount;
            seat.StreetBet += amount;
            seat.TotalCommitted += amount;
        }

        private void AfterAction()
        {
            // フォールドで1人残り → 即決着
            var survivors = _seats.Where(s => !s.HasFolded).ToList();
            if (survivors.Count == 1)
            {
                CompleteByFold(survivors[0]);
                return;
            }

            // 次の手番を探す。いなければストリート終了。
            int next = FindNextActor(CurrentSeatIndex);
            if (next >= 0)
            {
                CurrentSeatIndex = next;
                return;
            }
            AdvanceStreetOrShowdown();
        }

        /// <summary>from の次の座席から時計回りに、行動が必要なプレイヤーを探す。</summary>
        private int FindNextActor(int from)
        {
            int index = from;
            for (int i = 0; i < _seats.Count; i++)
            {
                index = NextSeat(index);
                var seat = _seats[index];
                if (seat.HasFolded || seat.IsAllIn)
                {
                    continue;
                }
                if (!seat.HasActed || seat.StreetBet < CurrentBet)
                {
                    return index;
                }
            }
            return -1;
        }

        private void AdvanceStreetOrShowdown()
        {
            while (true)
            {
                if (CurrentStreet == Street.River)
                {
                    DoShowdown();
                    return;
                }

                CurrentStreet = (Street)((int)CurrentStreet + 1);
                DealCommunity(CurrentStreet == Street.Flop ? 3 : 1);

                foreach (var seat in _seats)
                {
                    seat.StreetBet = 0;
                    seat.HasActed = false;
                }
                CurrentBet = 0;
                _lastRaiseSize = BigBlind;

                if (CountActiveNonAllIn() >= 2)
                {
                    CurrentSeatIndex = FindNextActor(ButtonIndex);
                    return;
                }
                // ベット可能なプレイヤーが1人以下 → ベッティングなしで次のストリートへ (ランアウト)
            }
        }

        private void DoShowdown()
        {
            CurrentStreet = Street.Showdown;
            CurrentSeatIndex = -1;

            // 役の評価
            var hands = new Dictionary<int, HandValue>();
            var cardBuffer = new List<Card>(7);
            foreach (var seat in _seats)
            {
                if (seat.HasFolded)
                {
                    continue;
                }
                cardBuffer.Clear();
                cardBuffer.AddRange(seat.HoleCards);
                cardBuffer.AddRange(_community);
                hands[seat.SeatIndex] = HandEvaluator.Evaluate(cardBuffer);
            }

            // 投入額の階層でポットを分割 (フォールドしたプレイヤーの死に金も各階層に含める)
            var liveLevels = _seats
                .Where(s => !s.HasFolded)
                .Select(s => s.TotalCommitted)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var pots = new List<PotResult>();
            var payouts = new int[_seats.Count];
            int allocated = 0;
            int prev = 0;
            for (int levelIndex = 0; levelIndex < liveLevels.Count; levelIndex++)
            {
                int level = liveLevels[levelIndex];
                int amount = 0;
                foreach (var seat in _seats)
                {
                    amount += Math.Clamp(seat.TotalCommitted - prev, 0, level - prev);
                }
                // 最後の階層に、想定外の未分配チップも合算する (チップ保存則の安全網)
                if (levelIndex == liveLevels.Count - 1)
                {
                    amount += Pot - allocated - amount;
                }
                allocated += amount;
                prev = level;
                if (amount <= 0)
                {
                    continue;
                }

                var eligible = _seats
                    .Where(s => !s.HasFolded && s.TotalCommitted >= level)
                    .Select(s => s.SeatIndex)
                    .ToList();
                var bestValue = eligible.Select(i => hands[i]).Max();
                // 端数チップはボタンの左隣から近い順に配る
                var winners = eligible
                    .Where(i => hands[i] == bestValue)
                    .OrderBy(i => (i - ButtonIndex - 1 + _seats.Count) % _seats.Count)
                    .ToList();

                int share = amount / winners.Count;
                int remainder = amount % winners.Count;
                for (int w = 0; w < winners.Count; w++)
                {
                    payouts[winners[w]] += share + (w < remainder ? 1 : 0);
                }
                pots.Add(new PotResult(amount, eligible, winners));
            }

            foreach (var seat in _seats)
            {
                seat.Stack += payouts[seat.SeatIndex];
            }

            Result = new HandResult(
                pots,
                payouts,
                _seats.Select(s => s.Stack).ToList(),
                wentToShowdown: true,
                showdownHands: hands);
            IsComplete = true;
        }

        private void CompleteByFold(SeatState winner)
        {
            CurrentSeatIndex = -1;
            var payouts = new int[_seats.Count];
            payouts[winner.SeatIndex] = Pot;
            winner.Stack += Pot;

            Result = new HandResult(
                new List<PotResult>
                {
                    new PotResult(Pot, new List<int> { winner.SeatIndex }, new List<int> { winner.SeatIndex }),
                },
                payouts,
                _seats.Select(s => s.Stack).ToList(),
                wentToShowdown: false,
                showdownHands: new Dictionary<int, HandValue>());
            IsComplete = true;
        }

        // ---- セットアップ ----

        private void DealHoleCards()
        {
            // ボタンの左隣から時計回りに1枚ずつ2周
            for (int round = 0; round < 2; round++)
            {
                int index = ButtonIndex;
                for (int i = 0; i < _seats.Count; i++)
                {
                    index = NextSeat(index);
                    _seats[index].SetHoleCard(round, _deck.Draw());
                }
            }
        }

        private void DealCommunity(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _community.Add(_deck.Draw());
            }
        }

        private void PostBlind(int seatIndex, int amount)
        {
            var seat = _seats[seatIndex];
            Commit(seat, Math.Min(amount, seat.Stack));
        }

        private int CountActiveNonAllIn()
        {
            int count = 0;
            foreach (var seat in _seats)
            {
                if (!seat.HasFolded && !seat.IsAllIn)
                {
                    count++;
                }
            }
            return count;
        }

        private int NextSeat(int from) => (from + 1) % _seats.Count;
    }
}
