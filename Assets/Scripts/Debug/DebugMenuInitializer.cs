#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Cysharp.Text;
using KTC.Core;
using KTC.Poker.Domain;
using KTC.SaveData;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityFramework.Debugging;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Scene
{
    /// <summary>
    /// デバッグメニューの項目登録 (エディタ / Development Build のみ)。
    /// 開閉: F3 キー。
    /// </summary>
    public static class DebugMenuInitializer
    {
        private static readonly OffsetTimeProvider DEBUG_TIME = new OffsetTimeProvider();
        private static string HandEvalResult = "(未実行)";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            RegisterItems(DebugMenuController.Instance);

            var hotkeyGo = new GameObject("[DebugMenuHotkey]");
            UnityEngine.Object.DontDestroyOnLoad(hotkeyGo);
            hotkeyGo.AddComponent<DebugMenuHotkey>();
        }

        private static void RegisterItems(DebugMenuController menu)
        {
            // ---- データ ----
            menu.AddLabel("データ", "所持チップ", () =>
            {
                var data = SaveDataService.CreateDefault().Load();
                return ZString.Format("{0:N0} ({1})", data.Chips, string.IsNullOrEmpty(data.PlayerName) ? "名前未設定" : data.PlayerName);
            });
            menu.AddButton("データ", "チップ +1000", () => ModifyChips(1000));
            menu.AddButton("データ", "チップ -1000", () => ModifyChips(-1000));
            menu.AddButton("データ", "チュートリアルフラグクリア", () =>
            {
                var service = SaveDataService.CreateDefault();
                var data = service.Load();
                data.TutorialFlags = 0;
                service.Save(data);
            });
            menu.AddButton("データ", "セーブデータ削除 (アカウント削除)", () =>
            {
                SaveDataService.CreateDefault().Delete();
                Debug.Log("[DebugMenu] セーブデータを削除しました。次回タイトルから初回フローに入ります。");
            });

            // ---- シーン ----
            foreach (SceneId sceneId in Enum.GetValues(typeof(SceneId)))
            {
                if (sceneId == SceneId.TransitionLoading) continue;
                var target = sceneId;
                menu.AddButton("シーン", ZString.Format("{0} へ", target), () =>
                {
                    DebugMenuController.Instance.Close();
                    _ = SceneController.Instance.LoadSceneWithFadeAsync(target, SceneIdExtensions.ToSceneName);
                });
            }

            // ---- 時刻 ----
            menu.AddLabel("時刻", "ゲーム内時刻 (UTC)", () => GameClock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            menu.AddLabel("時刻", "オフセット", () => DEBUG_TIME.Offset.ToString());
            menu.AddButton("時刻", "+1時間", () => AddTimeOffset(TimeSpan.FromHours(1)));
            menu.AddButton("時刻", "+1日", () => AddTimeOffset(TimeSpan.FromDays(1)));
            menu.AddButton("時刻", "リセット", () =>
            {
                DEBUG_TIME.Offset = TimeSpan.Zero;
                GameClock.Provider = new SystemTimeProvider();
            });

            // ---- カード ----
            menu.AddInput("カード", "乱数シード (0=無効)",
                () => DebugGameSettings.FixedSeed.ToString(),
                text => DebugGameSettings.FixedSeed = int.TryParse(text, out var seed) ? seed : 0);
            menu.AddInput("カード", "積み込みデッキ",
                () => DebugGameSettings.RiggedDeckText,
                text => DebugGameSettings.RiggedDeckText = text);
            menu.AddToggle("カード", "CPU手札公開",
                () => DebugGameSettings.RevealCpuCards,
                value => DebugGameSettings.RevealCpuCards = value);
            menu.AddToggle("カード", "オートプレイ",
                () => DebugGameSettings.AutoPlay,
                value => DebugGameSettings.AutoPlay = value);
            menu.AddToggle("カード", "演出スキップ",
                () => DebugGameSettings.SkipEffects,
                value => DebugGameSettings.SkipEffects = value);
            menu.AddInput("カード", "役判定 (5〜7枚)",
                () => "",
                EvaluateHand);
            menu.AddLabel("カード", "役判定結果", () => HandEvalResult);

            // ---- 表示 ----
            menu.AddToggle("表示", "SafeArea模擬 (ノッチ端末)",
                () => UnityFramework.ScreenWatcher.SimulatedSafeAreaNormalized != null,
                value => UnityFramework.ScreenWatcher.SimulatedSafeAreaNormalized =
                    value ? new Rect(0.06f, 0.06f, 0.88f, 0.94f) : (Rect?)null);
            menu.AddLabel("表示", "実効SafeArea", () => UnityFramework.ScreenWatcher.EffectiveSafeArea.ToString());
            menu.AddLabel("表示", "解像度", () => ZString.Format("{0}x{1}", Screen.width, Screen.height));

            // ---- 通信 (サーバー結合。RemoteGameSession は生徒課題) ----
            menu.AddToggle("通信", "接続先: サーバー",
                () => KTC.Scene.GameLaunch.UseRemoteSession,
                value => KTC.Scene.GameLaunch.UseRemoteSession = value);
            menu.AddInput("通信", "サーバーURL",
                () => KTC.Scene.GameLaunch.ServerUrl,
                value => KTC.Scene.GameLaunch.ServerUrl = value);
            menu.AddLabel("通信", "状態", () =>
                KTC.Scene.GameLaunch.UseRemoteSession
                    ? "サーバー (RemoteGameSession: 生徒課題)" : "ローカル (LocalGameSession)");

            // ---- WebView ----
            menu.AddButton("WebView", "開く (example.com)", () =>
            {
                DebugMenuController.Instance.Close();
                _ = UnityFramework.WebViews.WebViewController.Instance.OpenAsync(
                    "https://example.com", new RectOffset(120, 120, 100, 100));
            });
            menu.AddButton("WebView", "開く (ルール: Wikipedia)", () =>
            {
                DebugMenuController.Instance.Close();
                _ = UnityFramework.WebViews.WebViewController.Instance.OpenAsync(
                    "https://ja.wikipedia.org/wiki/テキサス・ホールデム", new RectOffset(120, 120, 100, 100));
            });
            menu.AddButton("WebView", "閉じる", () => UnityFramework.WebViews.WebViewController.Instance.Close());
            menu.AddLabel("WebView", "状態", () =>
                UnityFramework.WebViews.WebViewController.HasInstance && UnityFramework.WebViews.WebViewController.Instance.IsOpen
                    ? "表示中" : "非表示");

            // ---- 情報 ----
            menu.AddLabel("情報", "バージョン", () => Application.version);
            menu.AddLabel("情報", "Unity", () => Application.unityVersion);
            menu.AddLabel("情報", "プラットフォーム", () => Application.platform.ToString());
            menu.AddLabel("情報", "ビルドGUID", () => Application.buildGUID);
        }

        private static void ModifyChips(long delta)
        {
            var service = SaveDataService.CreateDefault();
            var data = service.Load();
            data.Chips = Math.Max(0, data.Chips + delta);
            service.Save(data);
        }

        private static void AddTimeOffset(TimeSpan delta)
        {
            DEBUG_TIME.Offset += delta;
            GameClock.Provider = DEBUG_TIME;
        }

        private static void EvaluateHand(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            try
            {
                var cards = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Card.Parse)
                    .ToArray();
                var value = HandEvaluator.Evaluate(cards);
                HandEvalResult = ZString.Format("{0} {1}", value.DisplayName, value);
            }
            catch (Exception e)
            {
                HandEvalResult = ZString.Format("エラー: {0}", e.Message);
            }
        }
    }

    /// <summary>F3 でデバッグメニューを開閉するホットキー。</summary>
    public class DebugMenuHotkey : MonoBehaviour
    {
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
            {
                DebugMenuController.Instance.Toggle();
            }
        }
    }
}
#endif
