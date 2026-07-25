using System.Collections.Generic;
using Cysharp.Text;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using KTC.Poker.Session;
using KTC.SaveData;
using KTC.UI;
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
    /// LocalGameSession を RemoteGameSession に差し替えてもこのクラスは無変更で動く。
    ///
    /// 描画は2層構成:
    /// - 3D層: テーブル上のカード (斜め投影カメラで見る。シーン側に配置された Table の上に生成)
    /// - UI層: 席情報パネル・アクションボタン・ポット等 (Screen Space Overlay)
    /// </summary>
    public class InGameTable : MonoBehaviour, IScenePreparer
    {
        [SerializeField] private Canvas canvas;

        private const string FontAddress = "Fonts/NotoSansJP";
        private static readonly string[] StreetNames = { "プリフロップ", "フロップ", "ターン", "リバー", "ショーダウン" };

        // 3D配置 (シーンの Table に合わせた座標)
        private const float CardY = 0.235f;            // フェルト上面 (0.2) の少し上
        private const float SeatRadiusX = 3.9f;
        private const float SeatRadiusZ = 2.35f;
        private const float HoleCardGap = 0.36f;

        private IGameSession _session;
        private TableStateMessage _lastState;
        private TMP_FontAsset _font;
        private string _playerName = "あなた";
        private bool _isLeaving;
        private float _errorClearAt;
        private float _nextAutoActionAt;
        private bool _prepared;

        private Transform _cardsRoot;
        private readonly List<SeatView> _seatViews = new List<SeatView>();
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

        /// <summary>
        /// フェードインで見せる前の準備 (SceneController から呼ばれる)。
        /// セッション接続まで済ませるので、画面が見えた瞬間には配牌済みの卓が表示される。
        /// </summary>
        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            _font = await ResourceController.Instance.LoadAsync<TMP_FontAsset>(FontAddress, cancellationToken);

            // カードテクスチャ52枚+裏面をプリロード (画面が見える前に完了する = 遷移ゲートの恩恵)
            await LoadCardTexturesAsync(cancellationToken);

            var data = SaveDataService.CreateDefault().Load();
            if (!string.IsNullOrEmpty(data.PlayerName))
            {
                _playerName = data.PlayerName;
            }

            var config = GameLaunch.NextConfig ?? new LocalGameSessionConfig();
            GameLaunch.NextConfig = null;

            BuildUi(config.SeatCount, config.MySeat);
            BuildTableCards(config.SeatCount, config.MySeat);

            _session = new LocalGameSession(config);
            _stateSubscription = _session.StateUpdated.Subscribe(OnStateUpdated);
            _errorSubscription = _session.ErrorOccurred.Subscribe(OnSessionError);
            _session.Connect();
        }

        private System.IDisposable _stateSubscription;
        private System.IDisposable _errorSubscription;

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
            var deck = new Deck(); // 整列済み52枚の列挙に利用
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

        private async void Start()
        {
            // SceneController を経由しない直接再生 (エディタ) 用フォールバック
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
            // オートプレイ (モンキーテスト): 自分の手番をチェック/コールで自動進行。
            // StateUpdated ハンドラ内からの SendAction は再入ガードで拒否されるため、Update で行う
            if (DebugGameSettings.AutoPlay && _session != null && _lastState != null
                && Time.unscaledTime >= _nextAutoActionAt)
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

        // ---- セッションイベント ----

        private void OnStateUpdated(TableStateMessage state)
        {
            _lastState = state;
            Render(state);
        }

        private void OnSessionError(string message)
        {
            _errorText.text = message;
            _errorClearAt = Time.unscaledTime + 3f;
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

        private void Render(TableStateMessage state)
        {
            for (int seat = 0; seat < state.seats.Length; seat++)
            {
                RenderSeat(_seatViews[seat], state.seats[seat], state);
            }

            for (int i = 0; i < 5; i++)
            {
                if (i < state.communityCards.Length)
                {
                    _communityViews[i].ShowFace(state.communityCards[i]);
                }
                else
                {
                    _communityViews[i].Hide();
                }
            }

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

            _resultPanel.SetActive(state.isComplete);
            if (state.isComplete)
            {
                _resultText.text = BuildResultText(state);
                _nextHandButton.gameObject.SetActive(!state.isGameOver);
                _toResultButton.gameObject.SetActive(state.isGameOver);
            }
        }

        private void RenderSeat(SeatView view, SeatStateMessage seat, TableStateMessage state)
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
                view.Cards[0].Hide();
                view.Cards[1].Hide();
                return;
            }

            view.StackText.text = ZString.Format("{0}", seat.stack);
            view.BetText.text = seat.streetBet > 0 ? ZString.Format("Bet {0}", seat.streetBet) : "";
            view.StateText.text = seat.folded ? "フォールド" : seat.allIn ? "オールイン" : "";

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

        // ---- 3D カード構築 ----

        private void BuildTableCards(int seatCount, int mySeat)
        {
            var rootGo = new GameObject("TableCards");
            _cardsRoot = rootGo.transform;

            // コミュニティカード (中央列)
            _communityViews = new Card3D[5];
            for (int i = 0; i < 5; i++)
            {
                _communityViews[i] = new Card3D(_cardsRoot,
                    new Vector3((i - 2) * 0.72f, CardY, 0.35f), 1f);
            }

            // 席ごとのホールカード (自分が常に手前 = 画面下)
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

            // 席情報パネル (カードは3D側に出すので情報のみ)
            for (int seat = 0; seat < seatCount; seat++)
            {
                int displayIndex = (seat - mySeat + seatCount) % seatCount;
                float angle = displayIndex * Mathf.PI * 2f / seatCount;
                var pos = new Vector2(Mathf.Sin(angle) * 760f, -Mathf.Cos(angle) * 380f + 40f);
                _seatViews.Add(new SeatView(root, seat, pos, _font));
            }

            // アクションボタン
            _foldButton = QuickUi.MakeButton("FoldButton", root, new Vector2(-585f, -480f), new Vector2(180f, 70f), "フォールド", _font, OnFold, out _);
            _checkCallButton = QuickUi.MakeButton("CheckCallButton", root, new Vector2(-390f, -480f), new Vector2(180f, 70f), "チェック", _font, OnCheckCall, out _checkCallLabel);
            _raiseButton = QuickUi.MakeButton("RaiseButton", root, new Vector2(-195f, -480f), new Vector2(180f, 70f), "レイズ", _font, OnRaise, out _raiseLabel);
            _allInButton = QuickUi.MakeButton("AllInButton", root, new Vector2(0f, -480f), new Vector2(180f, 70f), "オールイン", _font, OnAllIn, out _);

            // 退出 (左上)
            var leave = QuickUi.MakeButton("LeaveButton", root, new Vector2(-830f, 490f), new Vector2(180f, 64f), "退出", _font, OnLeave, out var leaveLabel);
            ((Image)leave.targetGraphic).color = QuickUi.Panel;
            leaveLabel.color = QuickUi.Text;

            // 結果オーバーレイ
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
        /// テクスチャは <see cref="SetSharedTextures"/> で事前登録し、
        /// 描画はテクスチャ差し替え (MaterialPropertyBlock) のみで行う。
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

            public static void SetSharedTextures(Dictionary<byte, Texture2D> faces, Texture2D back)
            {
                _faceTextures = faces;
                _backTexture = back;
            }

            public Card3D(Transform parent, Vector3 position, float scale)
            {
                EnsureMaterial();

                _root = new GameObject("Card3D");
                _root.transform.SetParent(parent, false);
                _root.transform.localPosition = position;
                _root.transform.localScale = Vector3.one * scale;

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Face";
                Destroy(quad.GetComponent<MeshCollider>());
                quad.transform.SetParent(_root.transform, false);
                // X+90 で水平に寝かせ、Y180 で「上辺がカメラ側 (-Z)」を向くようにする
                quad.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
                quad.transform.localScale = new Vector3(0.66f, 0.9f, 1f);
                _renderer = quad.GetComponent<MeshRenderer>();
                _renderer.sharedMaterial = _sharedMaterial;

                _root.SetActive(false);
            }

            public void ShowFace(byte value)
            {
                if (_faceTextures == null || !_faceTextures.TryGetValue(value, out var texture))
                {
                    ShowBack();
                    return;
                }
                Apply(texture);
            }

            public void ShowBack()
            {
                Apply(_backTexture);
            }

            public void Hide()
            {
                _root.SetActive(false);
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
