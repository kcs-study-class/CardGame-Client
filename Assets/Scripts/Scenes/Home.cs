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
    /// ホーム画面 v1。プレイヤー情報とゲームモード選択。
    /// オンライン対戦・フレンド戦はサーバー実装後に解放 (現状ロック表示)。
    /// </summary>
    public class Home : MonoBehaviour, IScenePreparer
    {
        [SerializeField] private Canvas canvas;

        private const string FontAddress = "Fonts/NotoSansJP";
        private bool _isTransitioning;
        private bool _prepared;

        /// <summary>フェードインで見せる前の準備 (SceneController から呼ばれる)。</summary>
        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            var font = await ResourceController.Instance.LoadAsync<TMP_FontAsset>(FontAddress, cancellationToken);
            var data = SaveDataService.CreateDefault().Load();
            BuildUi(font, data);
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
            if (ResourceController.HasInstance)
            {
                ResourceController.Instance.Release(FontAddress);
            }
        }

        private void BuildUi(TMP_FontAsset font, PlayerData data)
        {
            var root = canvas.transform;
            QuickUi.MakeBackground(root);

            // ---- ヘッダー: プレイヤー情報 ----
            var header = QuickUi.MakePanel("Header", root, new Vector2(0f, 480f), new Vector2(1920f, 120f), QuickUi.PanelDark);
            string displayName = string.IsNullOrEmpty(data.PlayerName) ? "プレイヤー" : data.PlayerName;
            QuickUi.MakeText("PlayerName", header.transform, new Vector2(-700f, 12f), new Vector2(400f, 44f), 34f,
                displayName, font, TextAlignmentOptions.MidlineLeft);
            var chips = QuickUi.MakeText("Chips", header.transform, new Vector2(-700f, -30f), new Vector2(400f, 36f), 26f,
                ZString.Format("チップ: {0:N0}", data.Chips), font, TextAlignmentOptions.MidlineLeft);
            chips.color = QuickUi.Accent;
            QuickUi.MakeText("HomeTitle", header.transform, new Vector2(0f, 0f), new Vector2(400f, 60f), 44f, "ホーム", font);

            // ---- モード選択 ----
            QuickUi.MakeButton("CpuButton", root, new Vector2(0f, 120f), new Vector2(520f, 110f),
                "CPU対戦", font, OnCpuBattle, out _);

            var online = QuickUi.MakeButton("OnlineButton", root, new Vector2(0f, -30f), new Vector2(520f, 110f),
                "オンライン対戦", font, null, out var onlineLabel);
            online.interactable = false;
            onlineLabel.text = "オンライン対戦 (未開放)";

            var friend = QuickUi.MakeButton("FriendButton", root, new Vector2(0f, -180f), new Vector2(520f, 110f),
                "フレンド戦", font, null, out var friendLabel);
            friend.interactable = false;
            friendLabel.text = "フレンド戦 (未開放)";

            var hint = QuickUi.MakeText("Hint", root, new Vector2(0f, -320f), new Vector2(900f, 40f), 22f,
                "オンライン系はサーバー実装後に解放されます", font);
            hint.color = new Color(0.55f, 0.55f, 0.60f, 1f);
        }

        private async void OnCpuBattle()
        {
            if (_isTransitioning)
            {
                return;
            }
            _isTransitioning = true;
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Lobby, SceneIdExtensions.ToSceneName);
        }
    }
}
