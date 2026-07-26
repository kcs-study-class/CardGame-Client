using Cysharp.Text;
using KTC.SaveData;
using KTC.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityFramework;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;
using UnityFramework.UI;
using UnityFramework.WebViews;

namespace KTC.Scene
{
    /// <summary>
    /// ホーム画面。UI はシーン配置 (エディタで調整する)。このクラスは参照とロジックのみ持つ。
    /// </summary>
    public class Home : MonoBehaviour, IScenePreparer
    {
        [Header("ヘッダー")]
        [SerializeField] private TMP_Text initialText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text chipsText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button settingsButton;

        [Header("メニュー")]
        [SerializeField] private Button cpuButton;
        [SerializeField] private Button rulesButton;
        [SerializeField] private Button closeRulesButton;

        [Header("ルール表示")]
        [SerializeField] private string rulesUrl = "https://ja.wikipedia.org/wiki/テキサス・ホールデム";

        private bool _prepared;
        private bool _isTransitioning;
        private bool _rulesOpen;

        private void Awake()
        {
            cpuButton.onClick.AddListener(OnCpuBattle);
            settingsButton.onClick.AddListener(OnOpenSettings);
            rulesButton.onClick.AddListener(OnOpenRules);
            closeRulesButton.onClick.AddListener(CloseRules);
            closeRulesButton.gameObject.SetActive(false);
        }

        /// <summary>フェードインで見せる前の準備 (SceneController から呼ばれる)。</summary>
        public async Awaitable PrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            if (_prepared)
            {
                return;
            }
            _prepared = true;
            RefreshPlayerInfo();
            await Awaitables.Completed;
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

        private void Update()
        {
            if (_rulesOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseRules();
            }
        }

        private void RefreshPlayerInfo()
        {
            var data = SaveDataService.CreateDefault().Load();
            string displayName = string.IsNullOrEmpty(data.PlayerName) ? "プレイヤー" : data.PlayerName;
            initialText.text = displayName.Substring(0, 1);
            nameText.text = displayName;
            levelText.text = ZString.Format("Lv.{0}", data.Level);
            chipsText.text = ZString.Format("チップ: {0:N0}", data.Chips);
            statsText.text = data.HandsPlayed > 0
                ? ZString.Format("対戦 {0} / ハンド {1} / 勝率 {2}%",
                    data.MatchesPlayed, data.HandsPlayed, data.HandsWon * 100 / data.HandsPlayed)
                : "戦績はまだありません";
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

        private async void OnOpenSettings()
        {
            var modal = await ModalController.Instance.OpenAsync<SettingsModal>("Modals/Settings");
            if (modal != null)
            {
                await modal.WaitUntilClosedAsync();
                RefreshPlayerInfo(); // 設定以外 (チップ等) の将来変更にも追従
            }
        }

        private async void OnOpenRules()
        {
            if (_rulesOpen)
            {
                return;
            }
            _rulesOpen = true;
            closeRulesButton.gameObject.SetActive(true);
            // 下部を空けて「閉じる」バーを見えるようにする (WebViewはuGUIより常に前面のため)
            bool opened = await WebViewController.Instance.OpenAsync(
                rulesUrl, new RectOffset(80, 80, 60, 170));
            if (!opened)
            {
                CloseRules();
            }
        }

        private void CloseRules()
        {
            _rulesOpen = false;
            if (WebViewController.HasInstance)
            {
                WebViewController.Instance.Close();
            }
            if (closeRulesButton != null)
            {
                closeRulesButton.gameObject.SetActive(false);
            }
        }
    }
}
