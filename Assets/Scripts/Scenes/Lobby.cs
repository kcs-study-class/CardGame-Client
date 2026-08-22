using Cysharp.Text;
using KTC.Poker.Session;
using KTC.SaveData;
using KTC.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;
using UnityFramework.UI;

namespace KTC.Scene
{
    /// <summary>
    /// ロビー (CPU対戦の卓設定)。UI はシーン配置 (エディタで調整する)。
    /// _seatButtons / _stackButtons は選択肢の値順 (2/4/6, 100/200/500) に並べてシーンで割り当てる。
    /// </summary>
    public class Lobby : MonoBehaviour, IScenePreparer
    {
        [Header("選択肢 (値順に割り当て)")]
        [SerializeField, FormerlySerializedAs("seatButtons")] private Button[] _seatButtons = null; // 2人 / 4人 / 6人
        [SerializeField, FormerlySerializedAs("stackButtons")] private Button[] _stackButtons = null; // 100 / 200 / 500

        [Header("ブラインド")]
        [SerializeField, FormerlySerializedAs("blindsButton")] private Button _blindsButton = null;
        [SerializeField, FormerlySerializedAs("blindsLabel")] private TextMeshProUGUI _blindsLabel = null;

        [Header("操作")]
        [SerializeField, FormerlySerializedAs("startButton")] private Button _startButton = null;
        [SerializeField, FormerlySerializedAs("backButton")] private Button _backButton = null;

        [Header("所持チップ表示")]
        [SerializeField, FormerlySerializedAs("chipsText")] private TMP_Text _chipsText = null;

        private static readonly int[] SEAT_OPTIONS = { 2, 4, 6 };
        private static readonly int[] STACK_OPTIONS = { 100, 200, 500 };

        private int _selectedSeats = 4;
        private int _selectedStack = 200;
        private int _selectedSmallBlind = 1;
        private int _selectedBigBlind = 2;
        private long _chips = 0;
        private bool _isTransitioning = false;
        private bool _prepared = false;
        private bool _blindsModalOpen = false;

        private void Awake()
        {
            for (int i = 0; i < _seatButtons.Length; i++)
            {
                int seats = SEAT_OPTIONS[i];
                _seatButtons[i].onClick.AddListener(() => { _selectedSeats = seats; RefreshSelection(); });
            }
            for (int i = 0; i < _stackButtons.Length; i++)
            {
                int stack = STACK_OPTIONS[i];
                _stackButtons[i].onClick.AddListener(() => { _selectedStack = stack; RefreshSelection(); });
            }
            _blindsButton.onClick.AddListener(OnOpenBlinds);
            _startButton.onClick.AddListener(OnStartBattle);
            _backButton.onClick.AddListener(OnBack);
        }

        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            _chips = SaveDataService.CreateDefault().Load().Chips;

            // 現在の選択がバイインできない場合は払える最大の選択肢へ落とす
            if (_selectedStack > _chips)
            {
                for (int i = STACK_OPTIONS.Length - 1; i >= 0; i--)
                {
                    if (STACK_OPTIONS[i] <= _chips)
                    {
                        _selectedStack = STACK_OPTIONS[i];
                        break;
                    }
                }
            }
            RefreshSelection();
            GameAudio.PlayBgm(GameAudio.MENU_BGM);
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
            for (int i = 0; i < _seatButtons.Length; i++)
            {
                ApplySelected(_seatButtons[i], SEAT_OPTIONS[i] == _selectedSeats);
            }
            for (int i = 0; i < _stackButtons.Length; i++)
            {
                bool affordable = STACK_OPTIONS[i] <= _chips;
                _stackButtons[i].interactable = affordable;
                ApplySelected(_stackButtons[i], affordable && STACK_OPTIONS[i] == _selectedStack);
            }
            _blindsLabel.text = ZString.Format("SB {0} / BB {1}", _selectedSmallBlind, _selectedBigBlind);

            // 初期スタック分をバイインとして所持チップから支払う
            bool canStart = _selectedStack <= _chips;
            _startButton.interactable = canStart;
            _chipsText.text = canStart
                ? ZString.Format("所持チップ: {0:N0} (バイイン {1})", _chips, _selectedStack)
                : ZString.Format("所持チップ: {0:N0} — チップが足りません", _chips);
            _chipsText.color = canStart ? QuickUi.Text : QuickUi.Warn;
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
                BlindsModal modal = await ModalController.Instance.OpenAsync<BlindsModal>("Modals/Blinds");
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

            if (GameLaunch.UseRemoteSession)
            {
                // サーバー対戦ではチップ管理 (バイイン/精算) はサーバーの責務。ローカルセーブは触らない
                _isTransitioning = true;
            }
            else
            {
                // バイインを所持チップから差し引く (精算は InGameTable が最終スタックを書き戻す)
                SaveDataService saveService = SaveDataService.CreateDefault();
                PlayerData data = saveService.Load();
                if (data.Chips < _selectedStack)
                {
                    _chips = data.Chips;
                    RefreshSelection();
                    return;
                }
                _isTransitioning = true;
                data.Chips -= _selectedStack;
                saveService.Save(data);
                GameLaunch.ChipsAtStake = true;
                GameLaunch.LastBuyIn = _selectedStack;
            }

            LocalGameSessionConfig config = new LocalGameSessionConfig {
                SeatCount = _selectedSeats,
                StartingStack = _selectedStack,
                SmallBlind = _selectedSmallBlind,
                BigBlind = _selectedBigBlind,
                MySeat = 0,
                BotPolicy = new SimpleBot(),
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
