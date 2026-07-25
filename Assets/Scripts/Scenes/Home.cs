using Cysharp.Text;
using KTC.SaveData;
using KTC.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityFramework.Resource;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;
using UnityFramework.UI;
using UnityFramework.WebViews;

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

        private TMP_FontAsset _font;
        private Button _closeRulesButton;
        private bool _rulesOpen;

        private void BuildUi(TMP_FontAsset font, PlayerData data)
        {
            _font = font;
            var root = canvas.transform;
            QuickUi.MakeBackground(root);

            // ---- ヘッダー: プレイヤー情報 ----
            var header = QuickUi.MakePanel("Header", root, new Vector2(0f, 480f), new Vector2(1920f, 120f), QuickUi.PanelDark);

            // アイコン枠 (画像はサーバー/カスタマイズ実装後。今は頭文字を表示)
            var iconFrame = QuickUi.MakePanel("IconFrame", header.transform, new Vector2(-850f, 0f), new Vector2(84f, 84f), QuickUi.Panel);
            string displayName = string.IsNullOrEmpty(data.PlayerName) ? "プレイヤー" : data.PlayerName;
            var initial = QuickUi.MakeText("Initial", iconFrame.transform, Vector2.zero, new Vector2(80f, 80f), 40f,
                displayName.Substring(0, 1), font);
            initial.color = QuickUi.Accent;

            QuickUi.MakeText("PlayerName", header.transform, new Vector2(-590f, 24f), new Vector2(400f, 40f), 32f,
                displayName, font, TextAlignmentOptions.MidlineLeft);
            QuickUi.MakeText("Level", header.transform, new Vector2(-590f, -8f), new Vector2(400f, 30f), 22f,
                ZString.Format("Lv.{0}", data.Level), font, TextAlignmentOptions.MidlineLeft);
            var chips = QuickUi.MakeText("Chips", header.transform, new Vector2(-590f, -38f), new Vector2(400f, 32f), 24f,
                ZString.Format("チップ: {0:N0}", data.Chips), font, TextAlignmentOptions.MidlineLeft);
            chips.color = QuickUi.Accent;
            QuickUi.MakeText("HomeTitle", header.transform, new Vector2(0f, 0f), new Vector2(400f, 60f), 44f, "ホーム", font);

            // 設定 (ヘッダー右端)
            var settings = QuickUi.MakeButton("SettingsButton", header.transform, new Vector2(830f, 0f), new Vector2(160f, 64f),
                "設定", font, OnOpenSettings, out var settingsLabel);
            ((Image)settings.targetGraphic).color = QuickUi.Panel;
            settingsLabel.color = QuickUi.Text;

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

            var hint = QuickUi.MakeText("Hint", root, new Vector2(0f, -430f), new Vector2(900f, 40f), 22f,
                "オンライン系はサーバー実装後に解放されます", font);
            hint.color = new Color(0.55f, 0.55f, 0.60f, 1f);

            // ルール説明 (WebView)
            var rules = QuickUi.MakeButton("RulesButton", root, new Vector2(0f, -330f), new Vector2(520f, 90f),
                "ルール説明", font, OnOpenRules, out var rulesLabel);
            ((Image)rules.targetGraphic).color = QuickUi.Panel;
            rulesLabel.color = QuickUi.Text;

            // WebView 表示中の「閉じる」バー (WebView のマージンで空けた下部に出す)
            _closeRulesButton = QuickUi.MakeButton("CloseRulesButton", root, new Vector2(0f, -470f), new Vector2(360f, 84f),
                "ルールを閉じる (ESC)", font, CloseRules, out _);
            _closeRulesButton.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_rulesOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseRules();
            }
        }

        private async void OnOpenSettings()
        {
            var modal = await ModalController.Instance.OpenAsync<SettingsModal>("Modals/Settings");
            if (modal != null)
            {
                await modal.WaitUntilClosedAsync();
                // 音量以外にチップ等も設定モーダル経由で変わり得るため、表示を作り直す
                RebuildAfterSettings();
            }
        }

        private void RebuildAfterSettings()
        {
            if (canvas == null || _font == null)
            {
                return;
            }
            for (int i = canvas.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(canvas.transform.GetChild(i).gameObject);
            }
            BuildUi(_font, SaveDataService.CreateDefault().Load());
        }

        private async void OnOpenRules()
        {
            if (_rulesOpen)
            {
                return;
            }
            _rulesOpen = true;
            _closeRulesButton.gameObject.SetActive(true);
            // 下部を空けて「閉じる」バーを見えるようにする (WebViewはuGUIより常に前面のため)
            bool opened = await WebViewController.Instance.OpenAsync(
                "https://ja.wikipedia.org/wiki/テキサス・ホールデム",
                new RectOffset(80, 80, 60, 170));
            if (!opened)
            {
                CloseRules(); // 外部ブラウザへフォールバックした場合はバーを片付ける
            }
        }

        private void CloseRules()
        {
            _rulesOpen = false;
            if (WebViewController.HasInstance)
            {
                WebViewController.Instance.Close();
            }
            if (_closeRulesButton != null)
            {
                _closeRulesButton.gameObject.SetActive(false);
            }
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
