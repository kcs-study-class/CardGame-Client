using KTC.Poker.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

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

        [Header("操作")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button backButton;

        private static readonly int[] SeatOptions = { 2, 4, 6 };
        private static readonly int[] StackOptions = { 100, 200, 500 };
        private const int SmallBlind = 1;
        private const int BigBlind = 2;

        private int _selectedSeats = 4;
        private int _selectedStack = 200;
        private bool _isTransitioning;
        private bool _prepared;

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
        }

        private static void ApplySelected(Button button, bool selected)
        {
            ((Image)button.targetGraphic).color = selected ? KTC.UI.QuickUi.Accent : KTC.UI.QuickUi.Panel;
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.color = selected ? KTC.UI.QuickUi.TextDark : KTC.UI.QuickUi.Text;
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
                SmallBlind = SmallBlind,
                BigBlind = BigBlind,
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
