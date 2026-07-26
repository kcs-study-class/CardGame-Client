using Cysharp.Text;
using KTC.Poker.Session;
using KTC.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;
using UnityFramework.UI;

namespace KTC.Scene
{
    /// <summary>
    /// ロビー (CPU対戦の卓設定)。UI はシーン配置 (エディタで調整する)。
    /// seatButtons / stackButtons は選択肢の値順 (2/4/6, 100/200/500) に並べてシーンで割り当てる。
    /// </summary>
    public class Lobby : MonoBehaviour, IScenePreparer
    {
        [Header("選択肢 (値順に割り当て)")]
        [SerializeField] private Button[] seatButtons;   // 2人 / 4人 / 6人
        [SerializeField] private Button[] stackButtons;  // 100 / 200 / 500

        [Header("ブラインド")]
        [SerializeField] private Button blindsButton;
        [SerializeField] private TextMeshProUGUI blindsLabel;

        [Header("操作")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button backButton;

        private static readonly int[] SeatOptions = { 2, 4, 6 };
        private static readonly int[] StackOptions = { 100, 200, 500 };

        private int _selectedSeats = 4;
        private int _selectedStack = 200;
        private int _selectedSmallBlind = 1;
        private int _selectedBigBlind = 2;
        private bool _isTransitioning;
        private bool _prepared;
        private bool _blindsModalOpen;

        private void Awake()
        {
            for (int i = 0; i < seatButtons.Length; i++)
            {
                int seats = SeatOptions[i];
                seatButtons[i].onClick.AddListener(() => { _selectedSeats = seats; RefreshSelection(); });
            }
            for (int i = 0; i < stackButtons.Length; i++)
            {
                int stack = StackOptions[i];
                stackButtons[i].onClick.AddListener(() => { _selectedStack = stack; RefreshSelection(); });
            }
            blindsButton.onClick.AddListener(OnOpenBlinds);
            startButton.onClick.AddListener(OnStartBattle);
            backButton.onClick.AddListener(OnBack);
        }

        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            RefreshSelection();
            await Awaitables.Completed;
        }

        private async void Start()
        {
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (!_prepared)
            {
                await PrepareAsync(destroyCancellationToken);
            }
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < seatButtons.Length; i++)
            {
                ApplySelected(seatButtons[i], SeatOptions[i] == _selectedSeats);
            }
            for (int i = 0; i < stackButtons.Length; i++)
            {
                ApplySelected(stackButtons[i], StackOptions[i] == _selectedStack);
            }
            blindsLabel.text = ZString.Format("SB {0} / BB {1}", _selectedSmallBlind, _selectedBigBlind);
        }

        private static void ApplySelected(Button button, bool selected)
        {
            QuickUi.SetSelected(button, selected);
        }

        /// <summary>ブラインド選択モーダルを開き、閉じられたら選択を反映する。</summary>
        private async void OnOpenBlinds()
        {
            if (_isTransitioning || _blindsModalOpen)
            {
                return;
            }
            _blindsModalOpen = true;
            try
            {
                var modal = await ModalController.Instance.OpenAsync<BlindsModal>("Modals/Blinds");
                if (modal == null)
                {
                    return;
                }
                modal.SetCurrent(_selectedSmallBlind, _selectedBigBlind);
                await modal.WaitUntilClosedAsync();
                _selectedSmallBlind = modal.SelectedSmallBlind;
                _selectedBigBlind = modal.SelectedBigBlind;
                RefreshSelection();
            }
            finally
            {
                _blindsModalOpen = false;
            }
        }

        private async void OnStartBattle()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;
            var config = new LocalGameSessionConfig
            {
                SeatCount = _selectedSeats,
                StartingStack = _selectedStack,
                SmallBlind = _selectedSmallBlind,
                BigBlind = _selectedBigBlind,
                MySeat = 0,
            };
            try
            {
                DebugGameSettings.Apply(config);
            }
            catch (System.Exception e)
            {
                SafeLogger.LogWarning($"[Lobby] デバッグ設定の適用に失敗 (無視して続行): {e.Message}");
            }
            GameLaunch.NextConfig = config;
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
