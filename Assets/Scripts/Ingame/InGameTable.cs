using System.Collections.Generic;
using Cysharp.Text;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using KTC.Poker.Session;
using KTC.SaveData;
using KTC.UI;
using LitMotion;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityFramework.Resource;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Scene
{
    /// <summary>
    /// 実対戦のテーブル画面。<see cref="IGameSession"/> のスナップショットを購読して描画するだけで、
    /// ゲーム状態は一切持たない (サーバーオーソリタティブ)。
    ///
    /// 受信スナップショットは即描画せず**プレゼンテーションキュー**に積み、演出速度で順に再生する:
    /// - Bot の行動結果は思考ディレイ付きで提示 (自分の行動結果は即時)
    /// - 新ハンドはカード配布アニメ、ストリート進行はコミュニティのフリップイン
    /// - ハンド終了はポット移動演出のあとに結果表示
    /// セッション側 (ローカル/リモート) はこの仕組みを知らない = シーム無変更。
    /// </summary>
    public class InGameTable : MonoBehaviour, IScenePreparer
    {
        [SerializeField] private Canvas canvas;
        [SerializeField, Tooltip("シーン配置のカードデッキ。配布アニメの発射元")] private Transform deckAnchor;

        [Header("演出設定")]
        [SerializeField, Tooltip("Bot思考ディレイの最小秒")] private float botThinkMin = 0.4f;
        [SerializeField, Tooltip("Bot思考ディレイの最大秒")] private float botThinkMax = 0.8f;
        [SerializeField, Tooltip("配布1枚の飛行時間")] private float dealDuration = 0.18f;
        [SerializeField, Tooltip("配布の1枚ごとの間隔")] private float dealInterval = 0.06f;
        [SerializeField, Tooltip("カードフリップ時間 (片面)")] private float flipDuration = 0.09f;
        [SerializeField, Tooltip("ポット移動演出の時間")] private float potFlyDuration = 0.55f;

        private const string FontAddress = "Fonts/NotoSansJP";
        private static readonly string[] StreetNames = { "プリフロップ", "フロップ", "ターン", "リバー", "ショーダウン" };

        // 3D配置 (シーンの Table に合わせた座標)
        private const float CardY = 0.235f;
        private const float SeatRadiusX = 3.9f;
        private const float SeatRadiusZ = 2.35f;
        private const float HoleCardGap = 0.36f;
        private static readonly Vector3 FallbackDeckPosition = new Vector3(0f, CardY + 0.06f, 1.55f);

        /// <summary>配布アニメの発射元 (シーンのデッキ位置。未設定なら中央奥)。</summary>
        private Vector3 DeckPosition => deckAnchor != null
            ? deckAnchor.position + new Vector3(0f, 0.06f, 0f)
            : FallbackDeckPosition;

        private IGameSession _session;
        private TMP_FontAsset _font;
        private string _playerName = "あなた";
        private bool _isLeaving;
        private float _errorClearAt;
        private float _nextAutoActionAt;
        private bool _prepared;
        private bool _effectsEnabledInSave = true;

        // プレゼンテーション
        private readonly Queue<TableStateMessage> _stateQueue = new Queue<TableStateMessage>();
        private bool _presenting;
        private TableStateMessage _presentedState; // 画面に反映済みの状態
        private TableStateMessage _lastState;       // 入力判定用 (= _presentedState)

        private System.IDisposable _stateSubscription;
        private System.IDisposable _errorSubscription;

        private Transform _cardsRoot;
        private readonly List<SeatView> _seatViews = new List<SeatView>();
        private readonly List<Vector2> _seatUiPositions = new List<Vector2>();
        private Card3D[] _communityViews;
        private TMP_Text _potText;
        private TMP_Text _statusText;
        private TMP_Text _errorText;
        private Button _foldButton;
        private Button _checkCallButton;
        private Button _raiseButton;
        private Button _allInButton;
        private TextMeshProUGUI _checkCallLabel;
        private TextMeshProUGUI _raiseLabel;
        private GameObject _resultPanel;
        private TMP_Text _resultText;
        private Button _nextHandButton;
        private Button _toResultButton;

        private bool EffectsOn => _effectsEnabledInSave && !DebugGameSettings.SkipEffects;

        // ---- 準備 ----

        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            _font = await ResourceController.Instance.LoadAsync<TMP_FontAsset>(FontAddress, cancellationToken);

            await LoadCardTexturesAsync(cancellationToken);

            var data = SaveDataService.CreateDefault().Load();
            if (!string.IsNullOrEmpty(data.PlayerName))
            {
                _playerName = data.PlayerName;
            }
            _effectsEnabledInSave = data.EffectsEnabled;

            var config = GameLaunch.NextConfig ?? new LocalGameSessionConfig();
            GameLaunch.NextConfig = null;

            BuildUi(config.SeatCount, config.MySeat);
            BuildTableCards(config.SeatCount, config.MySeat);

            _session = new LocalGameSession(config);
            _stateSubscription = _session.StateUpdated.Subscribe(OnStateUpdated);
            _errorSubscription = _session.ErrorOccurred.Subscribe(OnSessionError);
            _session.Connect();
        }

        private async void Start()
        {
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (!_prepared)
            {
                await PrepareAsync(destroyCancellationToken);
            }
        }

        private void OnDestroy()
        {
            _stateSubscription?.Dispose();
            _errorSubscription?.Dispose();
            if (_session != null)
            {
                _session.Dispose();
            }
            if (ResourceController.HasInstance)
            {
                ResourceController.Instance.Release(FontAddress);
                foreach (var address in _loadedCardAddresses)
                {
                    ResourceController.Instance.Release(address);
                }
            }
        }

        private void Update()
        {
            if (_errorText != null && _errorText.text.Length > 0 && Time.unscaledTime >= _errorClearAt)
            {
                _errorText.text = "";
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // オートプレイ: 提示済み状態ベースで進める (演出とキューを尊重した自然なペース)
            if (DebugGameSettings.AutoPlay && _session != null && _lastState != null
                && !_presenting && Time.unscaledTime >= _nextAutoActionAt)
            {
                if (_lastState.isComplete && !_lastState.isGameOver)
                {
                    _session.SendReady();
                    _nextAutoActionAt = Time.unscaledTime + 0.6f;
                }
                else if (_lastState.isYourTurn)
                {
                    OnCheckCall();
                    _nextAutoActionAt = Time.unscaledTime + 0.3f;
                }
            }
#endif
        }

        // ---- カードテクスチャ ----

        private const string CardBackAddress = "Cards/cardBack_red2.png";
        private readonly List<string> _loadedCardAddresses = new List<string>();

        private static string CardTextureAddress(Card card)
        {
            string suit = card.Suit == Suit.Spade ? "Spades"
                : card.Suit == Suit.Heart ? "Hearts"
                : card.Suit == Suit.Diamond ? "Diamonds" : "Clubs";
            string rank = card.Rank == Rank.Ace ? "A"
                : card.Rank == Rank.King ? "K"
                : card.Rank == Rank.Queen ? "Q"
                : card.Rank == Rank.Jack ? "J" : ((int)card.Rank).ToString();
            return ZString.Format("Cards/card{0}{1}.png", suit, rank);
        }

        private async Awaitable LoadCardTexturesAsync(System.Threading.CancellationToken cancellationToken)
        {
            var deck = new Deck();
            var textures = new Dictionary<byte, Texture2D>(52);
            var cards = new List<Card>(52);
            while (deck.Remaining > 0)
            {
                cards.Add(deck.Draw());
            }
            foreach (var card in cards)
            {
                _loadedCardAddresses.Add(CardTextureAddress(card));
            }
            _loadedCardAddresses.Add(CardBackAddress);

            await ResourceController.Instance.PreloadAllAsync<Texture2D>(_loadedCardAddresses, cancellationToken);

            foreach (var card in cards)
            {
                textures[card.Value] = ResourceController.Instance.Get<Texture2D>(CardTextureAddress(card));
            }
            Card3D.SetSharedTextures(textures, ResourceController.Instance.Get<Texture2D>(CardBackAddress));
        }

        // ---- セッションイベント → プレゼンテーションキュー ----

        private void OnStateUpdated(TableStateMessage state)
        {
            _stateQueue.Enqueue(state);
            if (!_presenting)
            {
                _ = PresentLoopAsync();
            }
        }

        private void OnSessionError(string message)
        {
            _errorText.text = message;
            _errorClearAt = Time.unscaledTime + 3f;
        }

        private async Awaitable PresentLoopAsync()
        {
            _presenting = true;
            try
            {
                while (_stateQueue.Count > 0)
                {
                    var next = _stateQueue.Dequeue();
                    await PresentAsync(_presentedState, next, destroyCancellationToken);
                    _presentedState = next;
                    _lastState = next;
                }
            }
            finally
            {
                _presenting = false;
            }
        }

        /// <summary>状態遷移1つぶんを演出付きで画面へ反映する。</summary>
        private async Awaitable PresentAsync(TableStateMessage prev, TableStateMessage next, System.Threading.CancellationToken ct)
        {
            bool effects = EffectsOn;

            // Bot の行動結果は「考えてから」提示する (自分の行動結果は即時)
            if (effects && prev != null && !prev.isComplete
                && prev.currentSeat >= 0 && prev.currentSeat != next.yourSeat)
            {
                await Awaitable.WaitForSecondsAsync(Random.Range(botThinkMin, botThinkMax), ct);
            }

            // 新ハンド → 配布演出
            bool newHand = prev == null || next.handNumber != prev.handNumber;
            if (newHand)
            {
                RenderTable(next, showCards: false, showResult: false);
                if (effects)
                {
                    await PlayDealAnimationAsync(next, ct);
                }
                ApplyCardStates(next);
                return;
            }

            // ハンド完了 → (ランアウト分のコミュニティ公開 →) ポット移動 → 結果表示
            // 注意: オールインのランアウトでは「コミュニティ追加」と「完了」が同一スナップショットで
            // 届くため、完了判定をストリート進行より先に行うこと (先に進行分岐へ入ると結果が出ない)
            if (next.isComplete && !prev.isComplete)
            {
                int fromCount = prev.communityCards.Length;
                RenderTable(next, showCards: true, showResult: false, communityCount: fromCount);
                if (effects && next.communityCards.Length > fromCount)
                {
                    await PlayCommunityRevealAsync(next, fromCount, ct);
                }
                ApplyCommunity(next, next.communityCards.Length);
                if (effects)
                {
                    await PlayPotAnimationAsync(next, ct);
                }
                RenderResultOverlay(next);
                return;
            }

            // ストリート進行 → 新しいコミュニティカードをフリップイン
            if (effects && next.communityCards.Length > prev.communityCards.Length)
            {
                RenderTable(next, showCards: true, showResult: false, communityCount: prev.communityCards.Length);
                await PlayCommunityRevealAsync(next, prev.communityCards.Length, ct);
                ApplyCommunity(next, next.communityCards.Length);
                return;
            }

            RenderTable(next, showCards: true, showResult: next.isComplete);
        }

        // ---- 操作 ----

        private void SendAction(int actionType, int amount = 0)
        {
            _session.SendAction(new PlayerActionMessage { actionType = actionType, amount = amount });
        }

        private void OnFold() => SendAction((int)ActionType.Fold);

        private void OnCheckCall()
        {
            if (_lastState == null || !_lastState.isYourTurn) return;
            SendAction(_lastState.actionRequest.canCheck ? (int)ActionType.Check : (int)ActionType.Call);
        }

        private void OnRaise()
        {
            if (_lastState == null || !_lastState.isYourTurn || !_lastState.actionRequest.canRaise) return;
            SendAction((int)ActionType.RaiseTo, _lastState.actionRequest.minRaiseTo);
        }

        private void OnAllIn()
        {
            if (_lastState == null || !_lastState.isYourTurn) return;
            var request = _lastState.actionRequest;
            if (request.canRaise)
            {
                SendAction((int)ActionType.RaiseTo, request.maxRaiseTo);
            }
            else if (request.canCall)
            {
                SendAction((int)ActionType.Call);
            }
        }

        private void OnNextHand() => _session.SendReady();

        private async void OnToResult()
        {
            if (_isLeaving) return;
            _isLeaving = true;
            GameLaunch.LastFinalState = _lastState;
            GameLaunch.LastMySeat = _session.MySeatIndex;
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Result, SceneIdExtensions.ToSceneName);
        }

        private async void OnLeave()
        {
            if (_isLeaving) return;
            _isLeaving = true;
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
        }

        // ---- 描画 ----

        /// <summary>盤面を state の内容で描画する。カード・結果表示は演出側の都合で抑制できる。</summary>
        private void RenderTable(TableStateMessage state, bool showCards, bool showResult, int communityCount = -1)
        {
            for (int seat = 0; seat < state.seats.Length; seat++)
            {
                RenderSeatInfo(_seatViews[seat], state.seats[seat], state);
                if (showCards)
                {
                    RenderSeatCards(_seatViews[seat], state.seats[seat]);
                }
                else
                {
                    _seatViews[seat].Cards[0].Hide();
                    _seatViews[seat].Cards[1].Hide();
                }
            }

            ApplyCommunity(state, communityCount >= 0 ? communityCount : (showCards ? state.communityCards.Length : 0));

            _potText.text = ZString.Format("POT {0}", state.pot);
            _statusText.text = ZString.Format("Hand #{0}  {1}", state.handNumber,
                state.isComplete ? "終了" : StreetNames[Mathf.Clamp(state.street, 0, StreetNames.Length - 1)]);

            bool showActions = state.isYourTurn && !state.isComplete;
            _foldButton.gameObject.SetActive(showActions);
            _checkCallButton.gameObject.SetActive(showActions);
            _raiseButton.gameObject.SetActive(showActions);
            _allInButton.gameObject.SetActive(showActions);
            if (showActions)
            {
                var request = state.actionRequest;
                _checkCallLabel.text = request.canCheck ? "チェック" : ZString.Format("コール {0}", request.callAmount);
                _raiseButton.interactable = request.canRaise;
                _raiseLabel.text = request.canRaise ? ZString.Format("レイズ {0}", request.minRaiseTo) : "レイズ不可";
            }

            if (showResult)
            {
                RenderResultOverlay(state);
            }
            else
            {
                _resultPanel.SetActive(false);
            }
        }

        private void ApplyCommunity(TableStateMessage state, int count)
        {
            for (int i = 0; i < 5; i++)
            {
                if (i < count && i < state.communityCards.Length)
                {
                    _communityViews[i].ShowFace(state.communityCards[i]);
                }
                else
                {
                    _communityViews[i].Hide();
                }
            }
        }

        private void ApplyCardStates(TableStateMessage state)
        {
            for (int seat = 0; seat < state.seats.Length; seat++)
            {
                RenderSeatCards(_seatViews[seat], state.seats[seat]);
            }
            ApplyCommunity(state, state.communityCards.Length);
        }

        private void RenderSeatInfo(SeatView view, SeatStateMessage seat, TableStateMessage state)
        {
            bool isTurn = !state.isComplete && state.currentSeat == seat.seat;
            view.Frame.color = isTurn ? QuickUi.Accent : QuickUi.Panel;

            string badge = seat.seat == state.buttonSeat ? " [D]"
                : seat.seat == state.smallBlindSeat ? " [SB]"
                : seat.seat == state.bigBlindSeat ? " [BB]" : "";
            string name = seat.seat == state.yourSeat ? _playerName : ZString.Format("CPU {0}", seat.seat);
            view.NameText.text = ZString.Concat(name, badge);

            if (seat.sittingOut)
            {
                view.StackText.text = "着席なし";
                view.BetText.text = "";
                view.StateText.text = "";
                return;
            }
            view.StackText.text = ZString.Format("{0}", seat.stack);
            view.BetText.text = seat.streetBet > 0 ? ZString.Format("Bet {0}", seat.streetBet) : "";
            view.StateText.text = seat.folded ? "フォールド" : seat.allIn ? "オールイン" : "";
        }

        private void RenderSeatCards(SeatView view, SeatStateMessage seat)
        {
            if (seat.sittingOut)
            {
                view.Cards[0].Hide();
                view.Cards[1].Hide();
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                byte value = i < seat.holeCards.Length ? seat.holeCards[i] : (byte)0;
                if (seat.folded)
                {
                    view.Cards[i].Hide();
                }
                else if (value == 0)
                {
                    view.Cards[i].ShowBack();
                }
                else
                {
                    view.Cards[i].ShowFace(value);
                }
            }
        }

        private void RenderResultOverlay(TableStateMessage state)
        {
            _resultPanel.SetActive(true);
            _resultText.text = BuildResultText(state);
            _nextHandButton.gameObject.SetActive(!state.isGameOver);
            _toResultButton.gameObject.SetActive(state.isGameOver);
        }

        private string BuildResultText(TableStateMessage state)
        {
            using (var sb = ZString.CreateStringBuilder())
            {
                var result = state.result;
                foreach (var pot in result.pots)
                {
                    sb.Append(ZString.Format("ポット {0} → ", pot.amount));
                    for (int i = 0; i < pot.winnerSeats.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        int seat = pot.winnerSeats[i];
                        sb.Append(seat == state.yourSeat ? _playerName : ZString.Format("CPU {0}", seat));
                    }
                    sb.AppendLine();
                }
                foreach (var hand in result.showdownHands)
                {
                    string name = hand.seat == state.yourSeat ? _playerName : ZString.Format("CPU {0}", hand.seat);
                    var category = (HandCategory)hand.category;
                    sb.AppendLine(ZString.Format("{0}: {1}", name, category.ToDisplayName()));
                }
                if (state.isGameOver)
                {
                    sb.AppendLine("--- 対戦終了 ---");
                }
                return sb.ToString();
            }
        }

        // ---- 演出 ----

        /// <summary>新ハンドの配布演出: デッキ位置から各席へ1枚ずつ飛ばし、公開カードはフリップ。</summary>
        private async Awaitable PlayDealAnimationAsync(TableStateMessage state, System.Threading.CancellationToken ct)
        {
            // 配布順はエンジンと同じ「ボタン左隣から時計回りに2周」
            var order = new List<(SeatView view, int cardIndex, SeatStateMessage seat)>();
            for (int round = 0; round < 2; round++)
            {
                int index = state.buttonSeat;
                for (int i = 0; i < state.seats.Length; i++)
                {
                    index = (index + 1) % state.seats.Length;
                    var seat = state.seats[index];
                    if (!seat.sittingOut && !seat.folded)
                    {
                        order.Add((_seatViews[index], round, seat));
                    }
                }
            }

            float delay = 0f;
            foreach (var (view, cardIndex, _) in order)
            {
                view.Cards[cardIndex].PlayDealFrom(DeckPosition, delay, dealDuration);
                delay += dealInterval;
            }
            await Awaitable.WaitForSecondsAsync(delay + dealDuration, ct);

            // 公開されている手札 (自分 / CPU手札公開デバッグ) をフリップ
            float flipDelay = 0f;
            foreach (var (view, cardIndex, seat) in order)
            {
                byte value = cardIndex < seat.holeCards.Length ? seat.holeCards[cardIndex] : (byte)0;
                if (value != 0)
                {
                    view.Cards[cardIndex].PlayFlipToFace(value, flipDelay, flipDuration);
                    flipDelay += 0.05f;
                }
            }
            if (flipDelay > 0f)
            {
                await Awaitable.WaitForSecondsAsync(flipDelay + flipDuration * 2f, ct);
            }
        }

        /// <summary>ストリート進行時: 追加されたコミュニティカードをフリップイン。</summary>
        private async Awaitable PlayCommunityRevealAsync(TableStateMessage state, int fromCount, System.Threading.CancellationToken ct)
        {
            float delay = 0f;
            for (int i = fromCount; i < state.communityCards.Length; i++)
            {
                _communityViews[i].PlayDealFrom(DeckPosition, delay, dealDuration);
                _communityViews[i].PlayFlipToFace(state.communityCards[i], delay + dealDuration, flipDuration);
                delay += dealInterval * 2f;
            }
            await Awaitable.WaitForSecondsAsync(delay + dealDuration + flipDuration * 2f, ct);
        }

        /// <summary>ハンド終了時: ポットの獲得額が勝者パネルへ飛ぶ。</summary>
        private async Awaitable PlayPotAnimationAsync(TableStateMessage state, System.Threading.CancellationToken ct)
        {
            var potOrigin = _potText.rectTransform.anchoredPosition;
            float wait = 0f;
            foreach (var pot in state.result.pots)
            {
                foreach (int winner in pot.winnerSeats)
                {
                    var fly = QuickUi.MakeText("PotFly", canvas.transform, potOrigin,
                        new Vector2(300f, 44f), 34f,
                        ZString.Format("+{0}", pot.amount / pot.winnerSeats.Length), _font);
                    fly.color = QuickUi.Accent;
                    var target = _seatUiPositions[winner];
                    LMotion.Create(potOrigin, target, potFlyDuration)
                        .WithEase(Ease.InOutQuad)
                        .Bind(fly, static (pos, text) => text.rectTransform.anchoredPosition = pos)
                        .AddTo(fly.gameObject);
                    LMotion.Create(1f, 0f, potFlyDuration)
                        .WithEase(Ease.InQuad)
                        .Bind(fly, static (alpha, text) => text.alpha = alpha)
                        .AddTo(fly.gameObject);
                    Destroy(fly.gameObject, potFlyDuration + 0.1f);
                }
                wait = potFlyDuration;
            }
            if (wait > 0f)
            {
                await Awaitable.WaitForSecondsAsync(wait + 0.15f, ct);
            }
        }

        // ---- 3D カード構築 ----

        private void BuildTableCards(int seatCount, int mySeat)
        {
            var rootGo = new GameObject("TableCards");
            _cardsRoot = rootGo.transform;

            _communityViews = new Card3D[5];
            for (int i = 0; i < 5; i++)
            {
                _communityViews[i] = new Card3D(_cardsRoot,
                    new Vector3((i - 2) * 0.72f, CardY, 0.35f), 1f);
            }

            for (int seat = 0; seat < seatCount; seat++)
            {
                int displayIndex = (seat - mySeat + seatCount) % seatCount;
                float angle = displayIndex * Mathf.PI * 2f / seatCount;
                var anchor = new Vector3(
                    Mathf.Sin(angle) * SeatRadiusX,
                    CardY,
                    -Mathf.Cos(angle) * SeatRadiusZ);
                float scale = displayIndex == 0 ? 1.15f : 0.95f;
                _seatViews[seat].Cards = new[]
                {
                    new Card3D(_cardsRoot, anchor + new Vector3(-HoleCardGap * scale, 0f, 0f), scale),
                    new Card3D(_cardsRoot, anchor + new Vector3(HoleCardGap * scale, 0f, 0f), scale),
                };
            }
        }

        // ---- UI 構築 ----

        private void BuildUi(int seatCount, int mySeat)
        {
            var root = canvas.transform;

            _potText = QuickUi.MakeText("Pot", root, new Vector2(0f, 240f), new Vector2(400f, 44f), 34f, "", _font);
            _potText.color = QuickUi.Accent;
            _statusText = QuickUi.MakeText("Status", root, new Vector2(0f, 500f), new Vector2(900f, 44f), 28f, "", _font);
            _errorText = QuickUi.MakeText("Error", root, new Vector2(0f, 455f), new Vector2(900f, 36f), 22f, "", _font);
            _errorText.color = QuickUi.Warn;

            for (int seat = 0; seat < seatCount; seat++)
            {
                int displayIndex = (seat - mySeat + seatCount) % seatCount;
                float angle = displayIndex * Mathf.PI * 2f / seatCount;
                var pos = new Vector2(Mathf.Sin(angle) * 760f, -Mathf.Cos(angle) * 380f + 40f);
                _seatUiPositions.Add(pos);
                _seatViews.Add(new SeatView(root, seat, pos, _font));
            }

            _foldButton = QuickUi.MakeButton("FoldButton", root, new Vector2(-585f, -480f), new Vector2(180f, 70f), "フォールド", _font, OnFold, out _);
            _checkCallButton = QuickUi.MakeButton("CheckCallButton", root, new Vector2(-390f, -480f), new Vector2(180f, 70f), "チェック", _font, OnCheckCall, out _checkCallLabel);
            _raiseButton = QuickUi.MakeButton("RaiseButton", root, new Vector2(-195f, -480f), new Vector2(180f, 70f), "レイズ", _font, OnRaise, out _raiseLabel);
            _allInButton = QuickUi.MakeButton("AllInButton", root, new Vector2(0f, -480f), new Vector2(180f, 70f), "オールイン", _font, OnAllIn, out _);

            var leave = QuickUi.MakeButton("LeaveButton", root, new Vector2(-830f, 490f), new Vector2(180f, 64f), "退出", _font, OnLeave, out var leaveLabel);
            ((Image)leave.targetGraphic).color = QuickUi.Panel;
            leaveLabel.color = QuickUi.Text;

            var resultPanel = QuickUi.MakePanel("ResultPanel", root, new Vector2(0f, 180f), new Vector2(760f, 300f), QuickUi.PanelDark);
            _resultPanel = resultPanel.gameObject;
            _resultText = QuickUi.MakeText("ResultText", resultPanel.transform, new Vector2(0f, 20f), new Vector2(700f, 240f), 26f, "", _font);
            _nextHandButton = QuickUi.MakeButton("NextHandButton", resultPanel.transform, new Vector2(0f, -110f), new Vector2(280f, 66f), "次のハンドへ", _font, OnNextHand, out _);
            _toResultButton = QuickUi.MakeButton("ToResultButton", resultPanel.transform, new Vector2(0f, -110f), new Vector2(280f, 66f), "結果へ", _font, OnToResult, out _);
            _resultPanel.SetActive(false);
        }

        private sealed class SeatView
        {
            public readonly Image Frame;
            public readonly TMP_Text NameText;
            public readonly TMP_Text StackText;
            public readonly TMP_Text BetText;
            public readonly TMP_Text StateText;
            public Card3D[] Cards;

            public SeatView(Transform parent, int seat, Vector2 pos, TMP_FontAsset font)
            {
                Frame = QuickUi.MakePanel(ZString.Format("Seat{0}", seat), parent, pos, new Vector2(250f, 108f), QuickUi.Panel);
                var inner = QuickUi.MakePanel("Inner", Frame.transform, Vector2.zero, new Vector2(242f, 100f), QuickUi.Bg);
                NameText = QuickUi.MakeText("Name", inner.transform, new Vector2(0f, 30f), new Vector2(230f, 34f), 22f, "", font);
                StackText = QuickUi.MakeText("Stack", inner.transform, new Vector2(-58f, -18f), new Vector2(120f, 30f), 22f, "", font);
                BetText = QuickUi.MakeText("Bet", inner.transform, new Vector2(58f, -18f), new Vector2(120f, 30f), 20f, "", font);
                BetText.color = QuickUi.Accent;
                StateText = QuickUi.MakeText("State", inner.transform, new Vector2(0f, -40f), new Vector2(200f, 28f), 18f, "", font);
                StateText.color = QuickUi.Warn;
            }
        }

        /// <summary>
        /// テーブル上の3Dカード (Kenney カードテクスチャの Quad)。
        /// 配布 (位置トゥイーン) とフリップ (X軸回転+テクスチャ差し替え) の演出付き。
        /// </summary>
        private sealed class Card3D
        {
            private static Material _sharedMaterial;
            private static Dictionary<byte, Texture2D> _faceTextures;
            private static Texture2D _backTexture;
            private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

            private readonly GameObject _root;
            private readonly MeshRenderer _renderer;
            private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
            private readonly Vector3 _homePosition;

            public static void SetSharedTextures(Dictionary<byte, Texture2D> faces, Texture2D back)
            {
                _faceTextures = faces;
                _backTexture = back;
            }

            public Card3D(Transform parent, Vector3 position, float scale)
            {
                EnsureMaterial();
                _homePosition = position;

                _root = new GameObject("Card3D");
                _root.transform.SetParent(parent, false);
                _root.transform.localPosition = position;
                _root.transform.localScale = Vector3.one * scale;

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Face";
                Destroy(quad.GetComponent<MeshCollider>());
                quad.transform.SetParent(_root.transform, false);
                quad.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
                quad.transform.localScale = new Vector3(0.66f, 0.9f, 1f);
                _renderer = quad.GetComponent<MeshRenderer>();
                _renderer.sharedMaterial = _sharedMaterial;

                _root.SetActive(false);
            }

            public void ShowFace(byte value)
            {
                ResetPose();
                if (_faceTextures == null || !_faceTextures.TryGetValue(value, out var texture))
                {
                    ShowBack();
                    return;
                }
                Apply(texture);
            }

            public void ShowBack()
            {
                ResetPose();
                Apply(_backTexture);
            }

            public void Hide()
            {
                _root.SetActive(false);
            }

            /// <summary>配布演出: from から定位置まで飛ぶ (裏面表示)。</summary>
            public void PlayDealFrom(Vector3 from, float delay, float duration)
            {
                ResetPose();
                Apply(_backTexture);
                _root.transform.localPosition = from;
                LMotion.Create(from, _homePosition, duration)
                    .WithDelay(delay)
                    .WithEase(Ease.OutQuad)
                    .Bind(_root.transform, static (pos, t) => t.localPosition = pos)
                    .AddTo(_root);
            }

            /// <summary>フリップ演出: 半回転で表面テクスチャへ差し替える。</summary>
            public void PlayFlipToFace(byte value, float delay, float halfDuration)
            {
                if (_faceTextures == null || !_faceTextures.TryGetValue(value, out var texture))
                {
                    return;
                }
                _root.SetActive(true);
                var self = this;
                LMotion.Create(0f, 90f, halfDuration)
                    .WithDelay(delay)
                    .WithEase(Ease.InQuad)
                    .WithOnComplete(() =>
                    {
                        self.Apply(texture);
                        LMotion.Create(90f, 0f, halfDuration)
                            .WithEase(Ease.OutQuad)
                            .Bind(self._root.transform, static (angle, t) =>
                                t.localRotation = Quaternion.Euler(angle, 0f, 0f))
                            .AddTo(self._root);
                    })
                    .Bind(_root.transform, static (angle, t) =>
                        t.localRotation = Quaternion.Euler(angle, 0f, 0f))
                    .AddTo(_root);
            }

            private void ResetPose()
            {
                _root.transform.localPosition = _homePosition;
                _root.transform.localRotation = Quaternion.identity;
            }

            private void Apply(Texture2D texture)
            {
                _root.SetActive(true);
                _propertyBlock.SetTexture(BaseMapId, texture);
                _renderer.SetPropertyBlock(_propertyBlock);
            }

            private static void EnsureMaterial()
            {
                if (_sharedMaterial != null)
                {
                    return;
                }
                _sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = Color.white,
                };
                _sharedMaterial.SetFloat("_Smoothness", 0.1f);
            }
        }
    }
}
