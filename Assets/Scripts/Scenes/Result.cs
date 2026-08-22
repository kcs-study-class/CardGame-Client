using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using KTC.SaveData;
using KTC.Poker.Protocol;
using UnityFramework.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Scene
{
    /// <summary>
    /// リザルト画面。UI はシーン配置。順位行は _rowTemplate を複製して並べる
    /// (テンプレート自体をエディタで調整できる)。
    /// </summary>
    public class Result : SceneBase
    {
        [SerializeField, FormerlySerializedAs("myRankText")] private TMP_Text _myRankText = null;
        [SerializeField, FormerlySerializedAs("profitText")] private TMP_Text _profitText = null;
        [SerializeField, FormerlySerializedAs("xpText")] private TMP_Text _xpText = null;
        [SerializeField, FormerlySerializedAs("rowsContainer")] private Transform _rowsContainer = null;
        [SerializeField, FormerlySerializedAs("rowTemplate")] private RectTransform _rowTemplate = null; // 非アクティブで配置しておく
        [SerializeField, FormerlySerializedAs("homeButton")] private Button _homeButton = null;


        private void Awake()
        {
            _homeButton.onClick.AddListener(OnGoHome);
        }

        protected override async Awaitable OnPrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (GameLaunch.LastFinalState != null)
            {
                BuildRanking();
            }
            GameAudio.PlayBgm(GameAudio.MENU_BGM);
            await Awaitables.Completed;
        }

        protected override async Awaitable OnStartAsync()
        {
            // 結果なしで直接開かれた場合はホームへ退避
            if (GameLaunch.LastFinalState == null)
            {
                await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
                return;
            }
            await base.OnStartAsync();
        }

        private void BuildRanking()
        {
            TableStateMessage state = GameLaunch.LastFinalState;
            int mySeat = GameLaunch.LastMySeat;
            PlayerData playerData = SaveDataService.CreateDefault().Load();
            string myName = string.IsNullOrEmpty(playerData.PlayerName) ? "あなた" : playerData.PlayerName;

            List<SeatStateMessage> ranking = state.seats
                .Where(s => !s.sittingOut || s.stack > 0)
                .OrderByDescending(s => s.stack)
                .ToList();

            int myRank = ranking.FindIndex(s => s.seat == mySeat) + 1;
            _myRankText.text = ZString.Format("あなたは {0}位!", myRank);

            // 収支 (バイインした対戦のみ。デバッグ起動は LastBuyIn=0 で非表示)
            if (GameLaunch.LastBuyIn > 0)
            {
                int myStack = 0;
                foreach (SeatStateMessage seat in state.seats)
                {
                    if (seat.seat == mySeat)
                    {
                        myStack = seat.stack;
                        break;
                    }
                }
                int profit = myStack - GameLaunch.LastBuyIn;
                _profitText.gameObject.SetActive(true);
                _profitText.text = profit >= 0
                    ? ZString.Format("収支 +{0:N0}", profit)
                    : ZString.Format("収支 {0:N0}", profit);
                if (profit > 0)
                {
                    _profitText.color = QuickUi.Accent;
                }
                else if (profit < 0)
                {
                    _profitText.color = QuickUi.Warn;
                }
                else
                {
                    _profitText.color = QuickUi.Text;
                }
            }
            else
            {
                _profitText.gameObject.SetActive(false);
            }

            if (GameLaunch.LastXpGained > 0)
            {
                _xpText.gameObject.SetActive(true);
                _xpText.text = GameLaunch.LastLeveledUp
                    ? ZString.Format("獲得XP +{0}  レベルアップ!", GameLaunch.LastXpGained)
                    : ZString.Format("獲得XP +{0}", GameLaunch.LastXpGained);
            }
            else
            {
                _xpText.gameObject.SetActive(false);
            }

            for (int i = 0; i < ranking.Count; i++)
            {
                SeatStateMessage seat = ranking[i];
                RectTransform row = Instantiate(_rowTemplate, _rowsContainer);
                row.gameObject.SetActive(true);
                bool isMe = seat.seat == mySeat;

                Image background = row.GetComponent<Image>();
                if (background != null)
                {
                    background.color = isMe ? QuickUi.Panel : QuickUi.PanelDark;
                }
                TMP_Text label = row.Find("Label")?.GetComponent<TMP_Text>();
                if (label != null)
                {
                    label.text = ZString.Format("{0}位  {1}", i + 1,
                        isMe ? myName : ZString.Format("CPU {0}", seat.seat));
                    if (isMe)
                    {
                        label.color = QuickUi.Accent;
                    }
                }
                TMP_Text stack = row.Find("Stack")?.GetComponent<TMP_Text>();
                if (stack != null)
                {
                    stack.text = ZString.Format("{0}", seat.stack);
                }
            }
        }

        private async void OnGoHome()
        {
            if (!TryBeginTransition())
            {
                return;
            }
            GameLaunch.ClearResult();
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
        }
    }
}
