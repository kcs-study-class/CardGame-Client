using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using KTC.SaveData;
using KTC.UI;
using TMPro;
using UnityEngine;
using UnityFramework.Resource;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Scene
{
    /// <summary>
    /// リザルト画面。直近の対戦の最終スタックから順位表を表示する。
    /// (チップの所持金連動はサーバー同期設計と合わせて後日)
    /// </summary>
    public class Result : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;

        private const string FontAddress = "Fonts/NotoSansJP";
        private bool _isTransitioning;

        private async void Start()
        {
            // 結果なしで直接開かれた場合はホームへ退避
            if (GameLaunch.LastFinalState == null)
            {
                await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Home, SceneIdExtensions.ToSceneName);
                return;
            }

            var font = await ResourceController.Instance.LoadAsync<TMP_FontAsset>(FontAddress, destroyCancellationToken);
            BuildUi(font);
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
            var state = GameLaunch.LastFinalState;
            int mySeat = GameLaunch.LastMySeat;
            var playerData = SaveDataService.CreateDefault().Load();
            string myName = string.IsNullOrEmpty(playerData.PlayerName) ? "あなた" : playerData.PlayerName;

            var root = canvas.transform;
            QuickUi.MakeBackground(root);
            QuickUi.MakeText("Title", root, new Vector2(0f, 430f), new Vector2(600f, 80f), 52f, "リザルト", font);

            // 最終スタック降順で順位表
            var ranking = state.seats
                .Where(s => !s.sittingOut || s.stack > 0)
                .OrderByDescending(s => s.stack)
                .ToList();

            int myRank = ranking.FindIndex(s => s.seat == mySeat) + 1;
            var rankText = QuickUi.MakeText("MyRank", root, new Vector2(0f, 310f), new Vector2(700f, 70f), 42f,
                ZString.Format("あなたは {0}位!", myRank), font);
            rankText.color = QuickUi.Accent;

            for (int i = 0; i < ranking.Count; i++)
            {
                var seat = ranking[i];
                string name = seat.seat == mySeat ? myName : ZString.Format("CPU {0}", seat.seat);
                var row = QuickUi.MakePanel(ZString.Format("Row{0}", i), root,
                    new Vector2(0f, 180f - i * 86f), new Vector2(720f, 74f),
                    seat.seat == mySeat ? QuickUi.Panel : QuickUi.PanelDark);
                var label = QuickUi.MakeText("Label", row.transform, new Vector2(-40f, 0f), new Vector2(560f, 60f), 30f,
                    ZString.Format("{0}位  {1}", i + 1, name), font, TextAlignmentOptions.MidlineLeft);
                if (seat.seat == mySeat)
                {
                    label.color = QuickUi.Accent;
                }
                QuickUi.MakeText("Stack", row.transform, new Vector2(240f, 0f), new Vector2(200f, 60f), 30f,
                    ZString.Format("{0}", seat.stack), font, TextAlignmentOptions.MidlineRight);
            }

            QuickUi.MakeButton("HomeButton", root, new Vector2(0f, -420f), new Vector2(400f, 96f),
                "ホームへ", font, OnGoHome, out _);
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
