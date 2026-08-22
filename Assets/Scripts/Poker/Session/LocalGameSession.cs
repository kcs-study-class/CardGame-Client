using System;
using System.Collections.Generic;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using R3;

namespace KTC.Poker.Session
{
    /// <summary>LocalGameSession の卓設定。</summary>
    public sealed class LocalGameSessionConfig
    {
        public int SeatCount = 4;
        public int StartingStack = 200;
        public int SmallBlind = 1;
        public int BigBlind = 2;
        /// <summary>自分の座席。それ以外は Bot が座る。</summary>
        public int MySeat = 0;
        /// <summary>null なら StartingStack で均一。テストや途中再開用に個別指定可。</summary>
        public int[] InitialStacks = null;
        /// <summary>0 なら毎回ランダム。</summary>
        public int RandomSeed = 0;
        /// <summary>Bot の思考ルーチン。null なら CallingBot。</summary>
        public IBotPolicy BotPolicy = null;

        // ---- デバッグ用 (本番のリモート対戦には存在しない設定) ----

        /// <summary>デバッグ: 全席のホールカードを公開する (リダクション無効化)。</summary>
        public bool RevealAllHoleCards = false;

        /// <summary>デバッグ/テスト: ハンド開始時のデッキを差し替える (積み込み)。null なら通常シャッフル。</summary>
        public Func<Deck> DeckFactory = null;
    }

    /// <summary>
    /// ローカル完結の <see cref="IGameSession"/> 実装。
    /// HandEngine と Bot を内蔵し、「サーバーがやること」をすべてローカルで再現する:
    /// アクション検証 → 状態更新 → 受信者視点でのリダクション → スナップショット配信。
    /// この挙動がそのまま Go サーバーの仕様になる (RemoteGameSession に差し替えても UI は無変更)。
    ///
    /// 注意: イベントは同期的に発火する。StateUpdated ハンドラの中から
    /// SendAction を呼び返すことはできない (エラー通知になる)。
    /// </summary>
    public sealed class LocalGameSession : IGameSession
    {
        private readonly LocalGameSessionConfig _config;
        private readonly Random _random;
        private readonly IBotPolicy _botPolicy;

        private readonly int[] _tableStacks;
        private int _tableButton = -1;
        private int _handNumber = 0;
        private bool _gameOver = false;
        private bool _disposed = false;
        private bool _dispatching = false;

        private HandEngine _engine = null;
        private readonly List<int> _engineToTable = new List<int>();

        /// <summary>テスト用: ハンド開始時のデッキを差し替える (null なら通常シャッフル)。</summary>
        internal Func<Deck> DeckFactory = null;

        public int MySeatIndex => _config.MySeat;
        public bool IsConnected { get; private set; }

        private readonly Subject<Unit> _connected = new Subject<Unit>();
        private readonly Subject<TableStateMessage> _stateUpdated = new Subject<TableStateMessage>();
        private readonly Subject<string> _errorOccurred = new Subject<string>();

        public Observable<Unit> Connected => _connected;
        public Observable<TableStateMessage> StateUpdated => _stateUpdated;
        public Observable<string> ErrorOccurred => _errorOccurred;

        public LocalGameSession(LocalGameSessionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            _config = config;
            if (config.SeatCount < 2 || config.SeatCount > 9)
            {
                throw new ArgumentException("座席数は 2〜9 にしてください。", nameof(config));
            }
            if (config.MySeat < 0 || config.MySeat >= config.SeatCount)
            {
                throw new ArgumentException("MySeat が座席数の範囲外です。", nameof(config));
            }
            if (config.InitialStacks != null && config.InitialStacks.Length != config.SeatCount)
            {
                throw new ArgumentException("InitialStacks の長さが座席数と一致しません。", nameof(config));
            }

            _tableStacks = new int[config.SeatCount];
            for (int i = 0; i < config.SeatCount; i++)
            {
                _tableStacks[i] = config.InitialStacks != null ? config.InitialStacks[i] : config.StartingStack;
            }
            int funded = 0;
            foreach (int stack in _tableStacks)
            {
                if (stack > 0) funded++;
            }
            if (funded < 2 || _tableStacks[config.MySeat] <= 0)
            {
                throw new ArgumentException("チップを持つ席が2つ以上 (自席を含む) 必要です。", nameof(config));
            }

            _random = config.RandomSeed == 0 ? new Random() : new Random(config.RandomSeed);
            _botPolicy = config.BotPolicy != null ? config.BotPolicy : new CallingBot();
        }

        // ---- IGameSession ----

        public void Connect()
        {
            if (_disposed || IsConnected)
            {
                RaiseError("すでに接続済みか破棄されています。");
                return;
            }
            IsConnected = true;
            _connected.OnNext(Unit.Default);
            StartHandInternal();
        }

        public void SendAction(PlayerActionMessage action)
        {
            if (!ValidateSessionState()) return;
            if (_dispatching)
            {
                RaiseError("StateUpdated ハンドラ内から SendAction は呼べません。");
                return;
            }
            if (_engine.IsComplete)
            {
                RaiseError("ハンドは終了しています。SendReady で次へ進んでください。");
                return;
            }
            if (EngineSeatToTable(_engine.CurrentSeatIndex) != MySeatIndex)
            {
                RaiseError("あなたの手番ではありません。");
                return;
            }

            PlayerAction domainAction;
            try
            {
                domainAction = ToDomainAction(action);
            }
            catch (Exception e)
            {
                RaiseError($"アクションが不正です: {e.Message}");
                return;
            }

            try
            {
                _engine.Apply(domainAction);
            }
            catch (InvalidOperationException e)
            {
                RaiseError(e.Message);
                return;
            }

            AfterEngineAction();
            RunBots();
        }

        public void SendReady()
        {
            if (!ValidateSessionState()) return;
            if (_dispatching)
            {
                RaiseError("StateUpdated ハンドラ内から SendReady は呼べません。");
                return;
            }
            if (!_engine.IsComplete)
            {
                RaiseError("ハンドがまだ進行中です。");
                return;
            }
            StartHandInternal();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _connected.OnCompleted();
            _stateUpdated.OnCompleted();
            _errorOccurred.OnCompleted();
            _connected.Dispose();
            _stateUpdated.Dispose();
            _errorOccurred.Dispose();
        }

        // ---- 進行 ----

        private bool ValidateSessionState()
        {
            if (_disposed || !IsConnected)
            {
                RaiseError("接続されていません。");
                return false;
            }
            if (_gameOver)
            {
                RaiseError("卓は終了しています。");
                return false;
            }
            if (_engine == null)
            {
                RaiseError("ハンドが開始されていません。");
                return false;
            }
            return true;
        }

        private void StartHandInternal()
        {
            // 参加者 (チップ保有席) の確定。
            // 注意: ゲームオーバー時は直前ハンドの表示に _engineToTable が必要なので、
            // 続行が確定するまで書き換えない。
            List<int> participants = new List<int>();
            for (int seat = 0; seat < _config.SeatCount; seat++)
            {
                if (_tableStacks[seat] > 0)
                {
                    participants.Add(seat);
                }
            }
            if (participants.Count < 2)
            {
                _gameOver = true;
                Broadcast();
                return;
            }
            _engineToTable.Clear();
            _engineToTable.AddRange(participants);

            _tableButton = NextFundedSeat(_tableButton);
            _handNumber++;

            int[] engineStacks = new int[_engineToTable.Count];
            for (int i = 0; i < _engineToTable.Count; i++)
            {
                engineStacks[i] = _tableStacks[_engineToTable[i]];
            }

            Deck deck;
            if (DeckFactory != null)
            {
                deck = DeckFactory();
            }
            else if (_config.DeckFactory != null)
            {
                deck = _config.DeckFactory();
            }
            else
            {
                deck = CreateShuffledDeck();
            }
            _engine = new HandEngine(
                _config.SmallBlind,
                _config.BigBlind,
                engineStacks,
                _engineToTable.IndexOf(_tableButton),
                deck);

            AfterEngineAction(); // ブラインド即オールイン等で開始直後に完了している場合も拾う
            RunBots();
        }

        private Deck CreateShuffledDeck()
        {
            Deck deck = new Deck();
            deck.Shuffle(_random);
            return deck;
        }

        /// <summary>エンジンの状態が動いた後の共通処理: スタック同期 + 配信。</summary>
        private void AfterEngineAction()
        {
            if (_engine.IsComplete)
            {
                IReadOnlyList<int> finals = _engine.Result.FinalStacks;
                for (int i = 0; i < _engineToTable.Count; i++)
                {
                    _tableStacks[_engineToTable[i]] = finals[i];
                }
            }
            Broadcast();
        }

        /// <summary>自分の手番かハンド終了まで Bot を進める。</summary>
        private void RunBots()
        {
            while (_engine != null && !_engine.IsComplete
                   && EngineSeatToTable(_engine.CurrentSeatIndex) != MySeatIndex)
            {
                int botTableSeat = EngineSeatToTable(_engine.CurrentSeatIndex);
                LegalActions legal = _engine.GetLegalActions();
                PlayerAction botAction;
                try
                {
                    PlayerActionMessage decided = _botPolicy.Decide(BuildState(botTableSeat), BuildActionRequest(legal));
                    botAction = ToDomainAction(decided);
                    _engine.Apply(botAction);
                }
                catch (Exception)
                {
                    // Bot の不正手はフォールド扱い (進行を止めない)
                    _engine.Apply(PlayerAction.Fold());
                }
                AfterEngineAction();
            }
        }

        private int NextFundedSeat(int from)
        {
            int seat = from;
            for (int i = 0; i < _config.SeatCount; i++)
            {
                seat = (seat + 1) % _config.SeatCount;
                if (_tableStacks[seat] > 0)
                {
                    return seat;
                }
            }
            throw new InvalidOperationException("チップを持つ席がありません。");
        }

        private int EngineSeatToTable(int engineSeat)
        {
            return engineSeat >= 0 && engineSeat < _engineToTable.Count ? _engineToTable[engineSeat] : -1;
        }

        private static PlayerAction ToDomainAction(PlayerActionMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            switch ((ActionType)message.actionType)
            {
                case ActionType.Fold: return PlayerAction.Fold();
                case ActionType.Check: return PlayerAction.Check();
                case ActionType.Call: return PlayerAction.Call();
                case ActionType.RaiseTo: return PlayerAction.RaiseTo(message.amount);
                default: throw new ArgumentException($"未知の actionType: {message.actionType}");
            }
        }

        private void RaiseError(string message)
        {
            _errorOccurred.OnNext(message);
        }

        // ---- スナップショット構築 ----

        private void Broadcast()
        {
            _dispatching = true;
            try
            {
                _stateUpdated.OnNext(BuildState(MySeatIndex));
            }
            finally
            {
                _dispatching = false;
            }
        }

        /// <summary>viewer の視点でリダクションした全量スナップショットを作る。</summary>
        private TableStateMessage BuildState(int viewerSeat)
        {
            TableStateMessage msg = new TableStateMessage {
                handNumber = _handNumber,
                yourSeat = viewerSeat,
                isGameOver = _gameOver,
                buttonSeat = _tableButton,
                currentSeat = -1,
                smallBlindSeat = -1,
                bigBlindSeat = -1,
                communityCards = Array.Empty<byte>(),
                seats = new SeatStateMessage[_config.SeatCount],
            };

            bool revealShowdown = _engine != null && _engine.IsComplete && _engine.Result.WentToShowdown;

            for (int seat = 0; seat < _config.SeatCount; seat++)
            {
                msg.seats[seat] = new SeatStateMessage
                {
                    seat = seat,
                    stack = _tableStacks[seat],
                    sittingOut = true,
                    holeCards = new byte[] { 0, 0 },
                };
            }

            if (_engine == null)
            {
                return msg;
            }

            msg.street = (int)_engine.CurrentStreet;
            msg.pot = _engine.Pot;
            msg.currentBet = _engine.CurrentBet;
            msg.currentSeat = EngineSeatToTable(_engine.CurrentSeatIndex);
            msg.smallBlindSeat = EngineSeatToTable(_engine.SmallBlindIndex);
            msg.bigBlindSeat = EngineSeatToTable(_engine.BigBlindIndex);
            msg.isComplete = _engine.IsComplete;

            IReadOnlyList<Card> community = _engine.CommunityCards;
            msg.communityCards = new byte[community.Count];
            for (int i = 0; i < community.Count; i++)
            {
                msg.communityCards[i] = community[i].Value;
            }

            for (int engineSeat = 0; engineSeat < _engine.Seats.Count; engineSeat++)
            {
                SeatState seatState = _engine.Seats[engineSeat];
                int tableSeat = _engineToTable[engineSeat];
                SeatStateMessage seatMsg = msg.seats[tableSeat];
                seatMsg.sittingOut = false;
                seatMsg.stack = seatState.Stack;
                seatMsg.streetBet = seatState.StreetBet;
                seatMsg.totalCommitted = seatState.TotalCommitted;
                seatMsg.folded = seatState.HasFolded;
                seatMsg.allIn = seatState.IsAllIn;

                // リダクション: 見せてよいのは「自分の手札」か「ショーダウンで公開された手札」だけ
                // (RevealAllHoleCards はデバッグ専用の全公開スイッチ)
                bool reveal = tableSeat == viewerSeat
                              || (revealShowdown && !seatState.HasFolded)
                              || _config.RevealAllHoleCards;
                if (reveal)
                {
                    seatMsg.holeCards = new byte[]
                    {
                        seatState.HoleCards[0].Value,
                        seatState.HoleCards[1].Value,
                    };
                }
            }

            if (!_engine.IsComplete && msg.currentSeat == viewerSeat)
            {
                msg.isYourTurn = true;
                msg.actionRequest = BuildActionRequest(_engine.GetLegalActions());
            }

            if (_engine.IsComplete)
            {
                msg.result = BuildResult();
            }

            return msg;
        }

        private static ActionRequestMessage BuildActionRequest(LegalActions legal)
        {
            return new ActionRequestMessage
            {
                canCheck = legal.CanCheck,
                canCall = legal.CanCall,
                canRaise = legal.CanRaise,
                callAmount = legal.CallAmount,
                minRaiseTo = legal.MinRaiseTo,
                maxRaiseTo = legal.MaxRaiseTo,
            };
        }

        private HandResultMessage BuildResult()
        {
            HandResult result = _engine.Result;
            HandResultMessage msg = new HandResultMessage {
                wentToShowdown = result.WentToShowdown,
                payouts = new int[_config.SeatCount],
                pots = new PotResultMessage[result.Pots.Count],
                showdownHands = new ShowdownHandMessage[result.ShowdownHands.Count],
            };

            for (int i = 0; i < result.Payouts.Count; i++)
            {
                msg.payouts[_engineToTable[i]] = result.Payouts[i];
            }

            for (int i = 0; i < result.Pots.Count; i++)
            {
                PotResult pot = result.Pots[i];
                msg.pots[i] = new PotResultMessage
                {
                    amount = pot.Amount,
                    eligibleSeats = MapSeats(pot.EligibleSeats),
                    winnerSeats = MapSeats(pot.WinnerSeats),
                };
            }

            int handIndex = 0;
            foreach (KeyValuePair<int, HandValue> pair in result.ShowdownHands)
            {
                SeatState seatState = _engine.Seats[pair.Key];
                msg.showdownHands[handIndex++] = new ShowdownHandMessage
                {
                    seat = _engineToTable[pair.Key],
                    category = (int)pair.Value.Category,
                    holeCards = new byte[]
                    {
                        seatState.HoleCards[0].Value,
                        seatState.HoleCards[1].Value,
                    },
                };
            }
            return msg;
        }

        private int[] MapSeats(IReadOnlyList<int> engineSeats)
        {
            int[] mapped = new int[engineSeats.Count];
            for (int i = 0; i < engineSeats.Count; i++)
            {
                mapped[i] = _engineToTable[engineSeats[i]];
            }
            return mapped;
        }
    }
}
