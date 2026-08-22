using Cysharp.Text;
using KTC.SaveData;
using KTC.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
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
    public class Home : SceneBase
    {
        [Header("ヘッダー")]
        [SerializeField, FormerlySerializedAs("initialText")] private TMP_Text _initialText = null;
        [SerializeField, FormerlySerializedAs("nameText")] private TMP_Text _nameText = null;
        [SerializeField, FormerlySerializedAs("levelText")] private TMP_Text _levelText = null;
        [SerializeField, FormerlySerializedAs("chipsText")] private TMP_Text _chipsText = null;
        [SerializeField, FormerlySerializedAs("statsText")] private TMP_Text _statsText = null;
        [SerializeField, FormerlySerializedAs("settingsButton")] private Button _settingsButton = null;

        [Header("メニュー")]
        [SerializeField, FormerlySerializedAs("cpuButton")] private Button _cpuButton = null;
        [SerializeField, FormerlySerializedAs("rulesButton")] private Button _rulesButton = null;
        [SerializeField, FormerlySerializedAs("closeRulesButton")] private Button _closeRulesButton = null;

        [Header("ルール表示")]
        [SerializeField, FormerlySerializedAs("rulesUrl")] private string _rulesUrl = "https://ja.wikipedia.org/wiki/テキサス・ホールデム";

        private bool _rulesOpen = false;

        private void Awake()
        {
            _cpuButton.onClick.AddListener(OnCpuBattle);
            _settingsButton.onClick.AddListener(OnOpenSettings);
            _rulesButton.onClick.AddListener(OnOpenRules);
            _closeRulesButton.onClick.AddListener(CloseRules);
            _closeRulesButton.gameObject.SetActive(false);
        }

        /// <summary>フェードインで見せる前の準備 (SceneBase 経由で SceneController から呼ばれる)。</summary>
        protected override async Awaitable OnPrepareAsync(System.Threading.CancellationToken cancellationToken)
        {
            RefreshPlayerInfo();
            GameAudio.PlayBgm(GameAudio.MENU_BGM);
            await Awaitables.Completed;
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
            PlayerData data = SaveDataService.CreateDefault().Load();
            string displayName = string.IsNullOrEmpty(data.PlayerName) ? "プレイヤー" : data.PlayerName;
            _initialText.text = displayName.Substring(0, 1);
            _nameText.text = displayName;
            _levelText.text = ZString.Format("Lv.{0}", data.Level);
            _chipsText.text = ZString.Format("チップ: {0:N0}", data.Chips);
            _statsText.text = data.HandsPlayed > 0
                ? ZString.Format("対戦 {0} / ハンド {1} / 勝率 {2}%",
                    data.MatchesPlayed, data.HandsPlayed, data.HandsWon * 100 / data.HandsPlayed)
                : "戦績はまだありません";
        }

        private async void OnCpuBattle()
        {
            if (!TryBeginTransition())
            {
                return;
            }
            await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Lobby, SceneIdExtensions.ToSceneName);
        }

        private async void OnOpenSettings()
        {
            SettingsModal modal = await ModalController.Instance.OpenAsync<SettingsModal>("Modals/Settings");
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
            _closeRulesButton.gameObject.SetActive(true);
            // 下部を空けて「閉じる」バーを見えるようにする (WebViewはuGUIより常に前面のため)
            bool opened = await WebViewController.Instance.OpenAsync(
                _rulesUrl, new RectOffset(80, 80, 60, 170));
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
            if (_closeRulesButton != null)
            {
                _closeRulesButton.gameObject.SetActive(false);
            }
        }
    }
}
