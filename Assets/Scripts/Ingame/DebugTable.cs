using System.Collections.Generic;
using KTC.Poker.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KTC.Scene
{
    /// <summary>
    /// InGame シーンの1人デバッグ卓。全席を自分で操作して HandEngine を検証する。
    /// UI は実行時に自動生成する (デバッグ用途のため、シーンには Canvas とこのコンポーネントだけ)。
    /// </summary>
    public class DebugTable : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("canvas")] private Canvas _canvas = null;

        [Header("卓設定")]
        [SerializeField, Range(2, 9), FormerlySerializedAs("seatCount")] private int _seatCount = 4;
        [SerializeField, FormerlySerializedAs("startingStack")] private int _startingStack = 200;
        [SerializeField, FormerlySerializedAs("smallBlind")] private int _smallBlind = 1;
        [SerializeField, FormerlySerializedAs("bigBlind")] private int _bigBlind = 2;
        [SerializeField, Tooltip("0 なら毎回ランダム。固定するとリプレイ可能"), FormerlySerializedAs("randomSeed")] private int _randomSeed = 0;

        private static readonly Color FELT_COLOR = new Color(0.055f, 0.055f, 0.086f, 1f);
        private static readonly Color PANEL_COLOR = new Color(0.125f, 0.125f, 0.173f, 1f);
        private static readonly Color ACCENT_COLOR = new Color(0.878f, 0.706f, 0.361f, 1f);
        private static readonly Color TEXT_COLOR = new Color(0.925f, 0.925f, 0.949f, 1f);
        private static readonly Color CARD_FACE_COLOR = new Color(0.941f, 0.902f, 0.824f, 1f);
        private static readonly Color CARD_BACK_COLOR = new Color(0.173f, 0.227f, 0.396f, 1f);
        private static readonly Color RED_SUIT_COLOR = new Color(0.78f, 0.16f, 0.16f, 1f);
        private static readonly Color BLACK_SUIT_COLOR = new Color(0.12f, 0.12f, 0.14f, 1f);

        private HandEngine _engine = null;
        private int[] _stacks = null;
        private int _buttonIndex = -1;
        private System.Random _random = null;
        private int _handNumber = 0;

        private readonly List<SeatView> _seatViews = new List<SeatView>();
        private CardView[] _communityViews = null;
        private TMP_Text _potText = null;
        private TMP_Text _statusText = null;
        private TMP_Text _resultText = null;
        private Button _foldButton = null;
        private Button _checkCallButton = null;
        private Button _minRaiseButton = null;
        private Button _allInButton = null;
        private Button _nextHandButton = null;
        private TMP_Text _checkCallLabel = null;
        private TMP_Text _minRaiseLabel = null;

        private void Start()
        {
            _random = _randomSeed == 0 ? new System.Random() : new System.Random(_randomSeed);
            _stacks = new int[_seatCount];
            for (int i = 0; i < _seatCount; i++)
            {
                _stacks[i] = _startingStack;
            }
            EnsureEventSystem();
            BuildUi();
            StartNextHand();
        }

        // ---- 進行 ----

        private void StartNextHand()
        {
            // 飛んだ席はデバッグ用に自動リバイ
            for (int i = 0; i < _seatCount; i++)
            {
                if (_stacks[i] <= 0)
                {
                    _stacks[i] = _startingStack;
                }
            }
            _handNumber++;
            _buttonIndex = (_buttonIndex + 1) % _seatCount;
            Deck deck = new Deck();
            deck.Shuffle(_random);
            _engine = new HandEngine(_smallBlind, _bigBlind, _stacks, _buttonIndex, deck);
            Render();
        }

        private void ApplyAction(PlayerAction action)
        {
            _engine.Apply(action);
            if (_engine.IsComplete)
            {
                IReadOnlyList<int> finals = _engine.Result.FinalStacks;
                for (int i = 0; i < _seatCount; i++)
                {
                    _stacks[i] = finals[i];
                }
            }
            Render();
        }

        private void OnFold() => ApplyAction(PlayerAction.Fold());

        private void OnCheckCall()
        {
            LegalActions legal = _engine.GetLegalActions();
            ApplyAction(legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call());
        }

        private void OnMinRaise()
        {
            LegalActions legal = _engine.GetLegalActions();
            if (legal.CanRaise)
            {
                ApplyAction(PlayerAction.RaiseTo(legal.MinRaiseTo));
            }
        }

        private void OnAllIn()
        {
            LegalActions legal = _engine.GetLegalActions();
            if (legal.CanRaise)
            {
                ApplyAction(PlayerAction.RaiseTo(legal.MaxRaiseTo));
            }
            else if (legal.CanCall)
            {
                ApplyAction(PlayerAction.Call());
            }
        }

        // ---- 描画 ----

        private void Render()
        {
            for (int i = 0; i < _seatCount; i++)
            {
                RenderSeat(_seatViews[i], _engine.Seats[i]);
            }

            for (int i = 0; i < 5; i++)
            {
                if (i < _engine.CommunityCards.Count)
                {
                    _communityViews[i].ShowFace(_engine.CommunityCards[i]);
                }
                else
                {
                    _communityViews[i].ShowBack();
                }
            }

            _potText.text = $"POT {_engine.Pot}";

            if (_engine.IsComplete)
            {
                // デバッグ卓は英語表記 (LiberationSans SDF が CJK 非対応のため)
                _statusText.text = $"Hand #{_handNumber}  -  {(_engine.Result.WentToShowdown ? "Showdown" : "Fold win")}";
                _resultText.text = BuildResultText();
                SetActionButtonsVisible(false);
                _nextHandButton.gameObject.SetActive(true);
            }
            else
            {
                LegalActions legal = _engine.GetLegalActions();
                _statusText.text = $"Hand #{_handNumber}  -  {_engine.CurrentStreet}  -  Seat {legal.SeatIndex} to act";
                _resultText.text = "";
                SetActionButtonsVisible(true);
                _nextHandButton.gameObject.SetActive(false);

                _checkCallLabel.text = legal.CanCheck ? "Check" : $"Call {legal.CallAmount}";
                _checkCallButton.interactable = legal.CanCheck || legal.CanCall;
                _minRaiseLabel.text = legal.CanRaise ? $"Raise {legal.MinRaiseTo}" : "Raise -";
                _minRaiseButton.interactable = legal.CanRaise;
                _allInButton.interactable = legal.CanRaise || legal.CanCall;
            }
        }

        private void RenderSeat(SeatView view, SeatState seat)
        {
            bool isTurn = !_engine.IsComplete && _engine.CurrentSeatIndex == seat.SeatIndex;
            string badge = "";
            if (seat.SeatIndex == _engine.ButtonIndex)
            {
                badge = " [D]";
            }
            else if (seat.SeatIndex == _engine.SmallBlindIndex)
            {
                badge = " [SB]";
            }
            else if (seat.SeatIndex == _engine.BigBlindIndex)
            {
                badge = " [BB]";
            }
            view.NameText.text = $"Seat {seat.SeatIndex}{badge}";
            view.StackText.text = $"Stack {seat.Stack}";
            view.BetText.text = seat.StreetBet > 0 ? $"Bet {seat.StreetBet}" : "";
            view.Frame.color = isTurn ? ACCENT_COLOR : PANEL_COLOR;

            string state = "";
            if (seat.HasFolded)
            {
                state = "FOLD";
            }
            else if (seat.IsAllIn)
            {
                state = "ALL-IN";
            }
            view.StateText.text = state;

            for (int i = 0; i < 2; i++)
            {
                if (seat.HasFolded)
                {
                    view.Cards[i].ShowBack();
                }
                else
                {
                    view.Cards[i].ShowFace(seat.HoleCards[i]);
                }
            }
        }

        private string BuildResultText()
        {
            HandResult result = _engine.Result;
            List<string> lines = new List<string>();
            foreach (PotResult pot in result.Pots)
            {
                string winners = string.Join(", ", pot.WinnerSeats);
                lines.Add($"Pot {pot.Amount} → Seat {winners}");
            }
            foreach (KeyValuePair<int, HandValue> pair in result.ShowdownHands)
            {
                // DisplayName (日本語) はCJKフォント導入後に切り替える
                string handName = pair.Value.IsRoyalFlush ? "RoyalFlush" : pair.Value.Category.ToString();
                lines.Add($"Seat {pair.Key}: {handName}");
            }
            return string.Join("\n", lines);
        }

        private void SetActionButtonsVisible(bool visible)
        {
            _foldButton.gameObject.SetActive(visible);
            _checkCallButton.gameObject.SetActive(visible);
            _minRaiseButton.gameObject.SetActive(visible);
            _allInButton.gameObject.SetActive(visible);
        }

        // ---- UI生成 ----

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(transform, false);
        }

        private void BuildUi()
        {
            Transform root = _canvas.transform;
            MakePanel("Background", root, Vector2.zero, Vector2.zero, FELT_COLOR, stretch: true);

            // コミュニティカード + ポット
            _communityViews = new CardView[5];
            for (int i = 0; i < 5; i++)
            {
                _communityViews[i] = MakeCard(root, new Vector2((i - 2) * 95f, 60f), new Vector2(84f, 118f), 40f);
            }
            _potText = MakeText("PotText", root, new Vector2(0f, -40f), new Vector2(400f, 44f), 34f, TextAlignmentOptions.Center);
            _potText.color = ACCENT_COLOR;

            // 席 (楕円配置、seat0 が真下)
            for (int i = 0; i < _seatCount; i++)
            {
                float angle = i * Mathf.PI * 2f / _seatCount;
                Vector2 pos = new Vector2(Mathf.Sin(angle) * 720f, -Mathf.Cos(angle) * 330f + 20f);
                _seatViews.Add(MakeSeatView(root, i, pos));
            }

            // ステータス・結果表示
            _statusText = MakeText("StatusText", root, new Vector2(0f, 500f), new Vector2(1200f, 44f), 30f, TextAlignmentOptions.Center);
            _resultText = MakeText("ResultText", root, new Vector2(0f, 180f), new Vector2(900f, 200f), 28f, TextAlignmentOptions.Center);
            _resultText.color = ACCENT_COLOR;

            // アクションボタン (右下)
            _foldButton = MakeButton("FoldButton", root, new Vector2(-570f, -480f), "Fold", OnFold, out _);
            _checkCallButton = MakeButton("CheckCallButton", root, new Vector2(-380f, -480f), "Check", OnCheckCall, out _checkCallLabel);
            _minRaiseButton = MakeButton("MinRaiseButton", root, new Vector2(-190f, -480f), "Raise", OnMinRaise, out _minRaiseLabel);
            _allInButton = MakeButton("AllInButton", root, new Vector2(0f, -480f), "All-In", OnAllIn, out _);
            _nextHandButton = MakeButton("NextHandButton", root, new Vector2(760f, -480f), "Next Hand", StartNextHand, out _);
        }

        private SeatView MakeSeatView(Transform parent, int index, Vector2 pos)
        {
            Image frame = MakePanel($"Seat{index}", parent, pos, new Vector2(250f, 150f), PANEL_COLOR);
            Image inner = MakePanel("Inner", frame.transform, Vector2.zero, new Vector2(242f, 142f), FELT_COLOR);
            SeatView view = new SeatView { Frame = frame };
            view.NameText = MakeText("Name", inner.transform, new Vector2(0f, 52f), new Vector2(230f, 34f), 24f, TextAlignmentOptions.Center);
            view.StackText = MakeText("Stack", inner.transform, new Vector2(-58f, 22f), new Vector2(120f, 30f), 22f, TextAlignmentOptions.Center);
            view.BetText = MakeText("Bet", inner.transform, new Vector2(58f, 22f), new Vector2(120f, 30f), 22f, TextAlignmentOptions.Center);
            view.BetText.color = ACCENT_COLOR;
            view.StateText = MakeText("State", inner.transform, new Vector2(72f, -34f), new Vector2(100f, 34f), 24f, TextAlignmentOptions.Center);
            view.StateText.color = RED_SUIT_COLOR;
            view.Cards = new[]
            {
                MakeCard(inner.transform, new Vector2(-70f, -34f), new Vector2(56f, 78f), 26f),
                MakeCard(inner.transform, new Vector2(-10f, -34f), new Vector2(56f, 78f), 26f),
            };
            return view;
        }

        private Image MakePanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color, bool stretch = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            RectTransform rt = (RectTransform)go.transform;
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text MakeText(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = 5;
            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = TEXT_COLOR;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private Button MakeButton(string name, Transform parent, Vector2 pos, string label, UnityEngine.Events.UnityAction onClick, out TMP_Text labelText)
        {
            Image image = MakePanel(name, parent, pos, new Vector2(170f, 64f), PANEL_COLOR);
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.2f, 0.2f, 0.28f, 1f);
            colors.pressedColor = ACCENT_COLOR;
            colors.disabledColor = new Color(0.09f, 0.09f, 0.12f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            labelText = MakeText("Label", image.transform, Vector2.zero, new Vector2(160f, 56f), 26f, TextAlignmentOptions.Center);
            labelText.text = label;
            return button;
        }

        private CardView MakeCard(Transform parent, Vector2 pos, Vector2 size, float fontSize)
        {
            Image bg = MakePanel("Card", parent, pos, size, CARD_BACK_COLOR);
            TMP_Text label = MakeText("Label", bg.transform, Vector2.zero, size, fontSize, TextAlignmentOptions.Center);
            return new CardView { Background = bg, Label = label };
        }

        private sealed class SeatView
        {
            public Image Frame = null;
            public TMP_Text NameText = null;
            public TMP_Text StackText = null;
            public TMP_Text BetText = null;
            public TMP_Text StateText = null;
            public CardView[] Cards = null;
        }

        private sealed class CardView
        {
            public Image Background = null;
            public TMP_Text Label = null;

            public void ShowFace(Card card)
            {
                Background.color = CARD_FACE_COLOR;
                Label.text = card.ToSymbolString();
                Label.color = card.IsRedSuit ? RED_SUIT_COLOR : BLACK_SUIT_COLOR;
            }

            public void ShowBack()
            {
                Background.color = CARD_BACK_COLOR;
                Label.text = "";
            }
        }
    }
}
