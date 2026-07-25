using System.Collections.Generic;
using Cysharp.Text;
using KTC.Poker.Session;
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
    /// ロビー (CPU対戦の卓設定)。人数・初期スタックを選んで対戦開始する。
    /// オンライン対戦時はこの画面がルーム選択に置き換わる想定。
    /// </summary>
    public class Lobby : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;

        private const string FontAddress = "Fonts/NotoSansJP";

        private static readonly int[] SeatOptions = { 2, 4, 6 };
        private static readonly int[] StackOptions = { 100, 200, 500 };
        private const int SmallBlind = 1;
        private const int BigBlind = 2;

        private int _selectedSeats = 4;
        private int _selectedStack = 200;
        private bool _isTransitioning;

        private readonly Dictionary<int, Image> _seatButtons = new Dictionary<int, Image>();
        private readonly Dictionary<int, Image> _stackButtons = new Dictionary<int, Image>();

        private async void Start()
        {
            var font = await ResourceController.Instance.LoadAsync<TMP_FontAsset>(FontAddress, destroyCancellationToken);
            BuildUi(font);
            RefreshSelection();
        }

        private void OnDestroy()
        {
            if (ResourceController.HasInstance)
            {
                ResourceController.Instance.Release(FontAddress);
            }
        }

        private void BuildUi(TMP_FontAsset font)
        {
            var root = canvas.transform;
            QuickUi.MakeBackground(root);
            QuickUi.MakeText("Title", root, new Vector2(0f, 440f), new Vector2(600f, 70f), 48f, "CPU対戦 - 卓設定", font);

            // ---- 人数 ----
            QuickUi.MakeText("SeatsLabel", root, new Vector2(-380f, 260f), new Vector2(300f, 50f), 32f,
                "プレイヤー数", font, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < SeatOptions.Length; i++)
            {
                int seats = SeatOptions[i];
                var button = QuickUi.MakeButton(ZString.Format("Seats{0}", seats), root,
                    new Vector2(-60f + i * 190f, 260f), new Vector2(170f, 80f),
                    ZString.Format("{0}人", seats), font, () => SelectSeats(seats), out _);
                _seatButtons[seats] = (Image)button.targetGraphic;
            }

            // ---- 初期スタック ----
            QuickUi.MakeText("StackLabel", root, new Vector2(-380f, 140f), new Vector2(300f, 50f), 32f,
                "初期スタック", font, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < StackOptions.Length; i++)
            {
                int stack = StackOptions[i];
                var button = QuickUi.MakeButton(ZString.Format("Stack{0}", stack), root,
                    new Vector2(-60f + i * 190f, 140f), new Vector2(170f, 80f),
                    ZString.Format("{0}", stack), font, () => SelectStack(stack), out _);
                _stackButtons[stack] = (Image)button.targetGraphic;
            }

            // ---- ブラインド (固定表示) ----
            QuickUi.MakeText("BlindsLabel", root, new Vector2(-380f, 30f), new Vector2(300f, 50f), 32f,
                "ブラインド", font, TextAlignmentOptions.MidlineRight);
            QuickUi.MakeText("BlindsValue", root, new Vector2(-15f, 30f), new Vector2(300f, 50f), 32f,
                ZString.Format("SB {0} / BB {1}", SmallBlind, BigBlind), font, TextAlignmentOptions.MidlineLeft);

            // ---- 開始 / 戻る ----
            QuickUi.MakeButton("StartButton", root, new Vector2(0f, -220f), new Vector2(520f, 110f),
                "対戦開始", font, OnStartBattle, out _);
            var back = QuickUi.MakeButton("BackButton", root, new Vector2(-760f, -460f), new Vector2(240f, 80f),
                "← ホームへ", font, OnBack, out var backLabel);
            ((Image)back.targetGraphic).color = QuickUi.Panel;
            backLabel.color = QuickUi.Text;
        }

        private void SelectSeats(int seats)
        {
            _selectedSeats = seats;
            RefreshSelection();
        }

        private void SelectStack(int stack)
        {
            _selectedStack = stack;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (var pair in _seatButtons)
            {
                pair.Value.color = pair.Key == _selectedSeats ? QuickUi.Accent : QuickUi.Panel;
                pair.Value.GetComponentInChildren<TextMeshProUGUI>().color =
                    pair.Key == _selectedSeats ? QuickUi.TextDark : QuickUi.Text;
            }
            foreach (var pair in _stackButtons)
            {
                pair.Value.color = pair.Key == _selectedStack ? QuickUi.Accent : QuickUi.Panel;
                pair.Value.GetComponentInChildren<TextMeshProUGUI>().color =
                    pair.Key == _selectedStack ? QuickUi.TextDark : QuickUi.Text;
            }
        }

        private async void OnStartBattle()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;
            GameLaunch.NextConfig = new LocalGameSessionConfig
            {
                SeatCount = _selectedSeats,
                StartingStack = _selectedStack,
                SmallBlind = SmallBlind,
                BigBlind = BigBlind,
                MySeat = 0,
            };
            await SceneController.Instance.LoadSceneViaTransitionSceneAsync(
                SceneId.InGame, SceneId.TransitionLoading, SceneIdExtensions.ToSceneName,
                minimumDuration: 0.8f);
        }

        private async void OnBack()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
        }
    }
}
