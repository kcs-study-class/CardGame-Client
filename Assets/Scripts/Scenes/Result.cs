using System.Linq;
using Cysharp.Text;
using KTC.SaveData;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Scene
{
    /// <summary>
    /// リザルト画面。UI はシーン配置。順位行は rowTemplate を複製して並べる
    /// (テンプレート自体をエディタで調整できる)。
    /// </summary>
    public class Result : MonoBehaviour, IScenePreparer
    {
        [SerializeField] private TMP_Text myRankText;
        [SerializeField] private TMP_Text profitText;
        [SerializeField] private TMP_Text xpText;
        [SerializeField] private Transform rowsContainer;
        [SerializeField] private RectTransform rowTemplate; // 非アクティブで配置しておく
        [SerializeField] private Button homeButton;

        private bool _isTransitioning;
        private bool _prepared;

        private void Awake()
        {
            homeButton.onClick.AddListener(OnGoHome);
        }

        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            if (GameLaunch.LastFinalState != null)
            {
                BuildRanking();
            }
            GameAudio.PlayBgm(GameAudio.MenuBgm);
            await Awaitables.Completed;
        }

        private async void Start()
        {
            // 結果なしで直接開かれた場合はホームへ退避
            if (GameLaunch.LastFinalState == null)
            {
                await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
                return;
            }
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (!_prepared)
            {
                await PrepareAsync(destroyCancellationToken);
            }
        }

        private void BuildRanking()
        {
            var state = GameLaunch.LastFinalState;
            int mySeat = GameLaunch.LastMySeat;
            var playerData = SaveDataService.CreateDefault().Load();
            string myName = string.IsNullOrEmpty(playerData.PlayerName) ? "あなた" : playerData.PlayerName;

            var ranking = state.seats
                .Where(s => !s.sittingOut || s.stack > 0)
                .OrderByDescending(s => s.stack)
                .ToList();

            int myRank = ranking.FindIndex(s => s.seat == mySeat) + 1;
            myRankText.text = ZString.Format("あなたは {0}位!", myRank);

            // 収支 (バイインした対戦のみ。デバッグ起動は LastBuyIn=0 で非表示)
            if (GameLaunch.LastBuyIn > 0)
            {
                int myStack = 0;
                foreach (var seat in state.seats)
                {
                    if (seat.seat == mySeat)
                    {
                        myStack = seat.stack;
                        break;
                    }
                }
                int profit = myStack - GameLaunch.LastBuyIn;
                profitText.gameObject.SetActive(true);
                profitText.text = profit >= 0
                    ? ZString.Format("収支 +{0:N0}", profit)
                    : ZString.Format("収支 {0:N0}", profit);
                profitText.color = profit > 0 ? KTC.UI.QuickUi.Accent
                    : profit < 0 ? KTC.UI.QuickUi.Warn
                    : KTC.UI.QuickUi.Text;
            }
            else
            {
                profitText.gameObject.SetActive(false);
            }

            if (GameLaunch.LastXpGained > 0)
            {
                xpText.gameObject.SetActive(true);
                xpText.text = GameLaunch.LastLeveledUp
                    ? ZString.Format("獲得XP +{0}  レベルアップ!", GameLaunch.LastXpGained)
                    : ZString.Format("獲得XP +{0}", GameLaunch.LastXpGained);
            }
            else
            {
                xpText.gameObject.SetActive(false);
            }

            for (int i = 0; i < ranking.Count; i++)
            {
                var seat = ranking[i];
                var row = Instantiate(rowTemplate, rowsContainer);
                row.gameObject.SetActive(true);
                bool isMe = seat.seat == mySeat;

                var background = row.GetComponent<Image>();
                if (background != null)
                {
                    background.color = isMe ? KTC.UI.QuickUi.Panel : KTC.UI.QuickUi.PanelDark;
                }
                var label = row.Find("Label")?.GetComponent<TMP_Text>();
                if (label != null)
                {
                    label.text = ZString.Format("{0}位  {1}", i + 1,
                        isMe ? myName : ZString.Format("CPU {0}", seat.seat));
                    if (isMe)
                    {
                        label.color = KTC.UI.QuickUi.Accent;
                    }
                }
                var stack = row.Find("Stack")?.GetComponent<TMP_Text>();
                if (stack != null)
                {
                    stack.text = ZString.Format("{0}", seat.stack);
                }
            }
        }

        private async void OnGoHome()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;
            GameLaunch.ClearResult();
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
        }
    }
}
