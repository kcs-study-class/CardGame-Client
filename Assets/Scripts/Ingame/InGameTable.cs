using System.Collections.Generic;
using Cysharp.Text;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using KTC.Poker.Session;
using KTC.SaveData;
using KTC.UI;
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
    /// </summary>
    public class InGameTable : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;

        private const string FontAddress = "Fonts/NotoSansJP";
        private static readonly string[] StreetNames = { "プリフロップ", "フロップ", "ターン", "リバー", "ショーダウン" };

        private IGameSession _session;
        private TableStateMessage _lastState;
        private TMP_FontAsset _font;
        private string _playerName = "あなた";
        private bool _isLeaving;
        private float _errorClearAt;

        private readonly List<SeatView> _seatViews = new List<SeatView>();
        private CardView[] _communityViews;
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

        private async void Start()
        {
            _font = await ResourceController.Instance.LoadAsync<TMP_FontAsset>(FontAddress, destroyCancellationToken);

            var data = SaveDataService.CreateDefault().Load();
            if (!string.IsNullOrEmpty(data.PlayerName))
            {
                _playerName = data.PlayerName;
            }

            var config = GameLaunch.NextConfig ?? new LocalGameSessionConfig();
            GameLaunch.NextConfig = null;

            BuildUi(config.SeatCount, config.MySeat);

            _session = new LocalGameSession(config);
            _session.StateUpdated += OnStateUpdated;
            _session.ErrorOccurred += OnSessionError;
            _session.Connect();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
            if (ResourceController.HasInstance)
            {
                ResourceController.Instance.Release(FontAddress);
            }
        }

        private void Update()
        {
            if (_errorText != null && _errorText.text.Length > 0 && Time.unscaledTime >= _errorClearAt)
            {
                _errorText.text = "";
            }
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
                    _communityViews[i].ShowBack();
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
            view.Root.SetActive(true);

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

        // ---- UI 構築 ----

        private void BuildUi(int seatCount, int mySeat)
        {
            var root = canvas.transform;
            QuickUi.MakeBackground(root);

            _communityViews = new CardView[5];
            for (int i = 0; i < 5; i++)
            {
                _communityViews[i] = new CardView(root, new Vector2((i - 2) * 95f, 70f), new Vector2(84f, 118f), 40f, _font);
            }
            _potText = QuickUi.MakeText("Pot", root, new Vector2(0f, -30f), new Vector2(400f, 44f), 34f, "", _font);
            _potText.color = QuickUi.Accent;
            _statusText = QuickUi.MakeText("Status", root, new Vector2(0f, 500f), new Vector2(900f, 44f), 28f, "", _font);
            _errorText = QuickUi.MakeText("Error", root, new Vector2(0f, 455f), new Vector2(900f, 36f), 22f, "", _font);
            _errorText.color = QuickUi.Warn;

            // 席: 自分が常に真下に来るよう表示位置を回転させる
            for (int seat = 0; seat < seatCount; seat++)
            {
                int displayIndex = (seat - mySeat + seatCount) % seatCount;
                float angle = displayIndex * Mathf.PI * 2f / seatCount;
                var pos = new Vector2(Mathf.Sin(angle) * 720f, -Mathf.Cos(angle) * 330f + 30f);
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
            public readonly GameObject Root;
            public readonly Image Frame;
            public readonly TMP_Text NameText;
            public readonly TMP_Text StackText;
            public readonly TMP_Text BetText;
            public readonly TMP_Text StateText;
            public readonly CardView[] Cards;

            public SeatView(Transform parent, int seat, Vector2 pos, TMP_FontAsset font)
            {
                Frame = QuickUi.MakePanel(ZString.Format("Seat{0}", seat), parent, pos, new Vector2(250f, 150f), QuickUi.Panel);
                Root = Frame.gameObject;
                var inner = QuickUi.MakePanel("Inner", Frame.transform, Vector2.zero, new Vector2(242f, 142f), QuickUi.Bg);
                NameText = QuickUi.MakeText("Name", inner.transform, new Vector2(0f, 52f), new Vector2(230f, 34f), 22f, "", font);
                StackText = QuickUi.MakeText("Stack", inner.transform, new Vector2(-58f, 22f), new Vector2(120f, 30f), 22f, "", font);
                BetText = QuickUi.MakeText("Bet", inner.transform, new Vector2(58f, 22f), new Vector2(120f, 30f), 20f, "", font);
                BetText.color = QuickUi.Accent;
                StateText = QuickUi.MakeText("State", inner.transform, new Vector2(66f, -34f), new Vector2(110f, 34f), 20f, "", font);
                StateText.color = QuickUi.Warn;
                Cards = new[]
                {
                    new CardView(inner.transform, new Vector2(-70f, -34f), new Vector2(56f, 78f), 26f, font),
                    new CardView(inner.transform, new Vector2(-10f, -34f), new Vector2(56f, 78f), 26f, font),
                };
            }
        }

        private sealed class CardView
        {
            private readonly Image _background;
            private readonly TMP_Text _label;

            public CardView(Transform parent, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font)
            {
                _background = QuickUi.MakePanel("Card", parent, pos, size, QuickUi.CardBack);
                _label = QuickUi.MakeText("Label", _background.transform, Vector2.zero, size, fontSize, "", font);
            }

            public void ShowFace(byte value)
            {
                Card card;
                if (!Card.TryFromValue(value, out card) || card.IsNone)
                {
                    ShowBack();
                    return;
                }
                _background.gameObject.SetActive(true);
                _background.color = QuickUi.CardFace;
                _label.text = card.ToSymbolString();
                _label.color = card.IsRedSuit ? QuickUi.RedSuit : QuickUi.TextDark;
            }

            public void ShowBack()
            {
                _background.gameObject.SetActive(true);
                _background.color = QuickUi.CardBack;
                _label.text = "";
            }

            public void Hide()
            {
                _background.gameObject.SetActive(false);
            }
        }
    }
}
