#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using Cysharp.Text;
using KTC.Poker.Domain;
using KTC.Poker.Protocol;
using KTC.Poker.Session;
using KTC.Scene;
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

namespace KTC.Debugging
{
    /// <summary>
    /// AI / 自動テストから Play 中のゲームを操作するためのファサード (エディタ / Development Build のみ)。
    ///
    /// Unity MCP の runCommand は Assembly-CSharp と UnityEngine しか参照できないため、
    /// UnityFramework (SceneController) や R3 に触れる操作はすべてこのクラスに集約し、
    /// runCommand 側は KTC.Debugging.AIDebugBridge の static メソッドだけを呼べばよいようにする。
    ///
    /// 想定フロー:
    ///   SetRemote(url, token) → StartInGame(...) → (Snapshot をポーリング) → Act(...) / Ready()
    /// </summary>
    public static class AIDebugBridge
    {
        /// <summary>接続先をサーバー (RemoteGameSession) に切り替える。token 付き URL を組み立てる。</summary>
        public static string SetRemote(string url, string token)
        {
            string finalUrl = url;
            if (!string.IsNullOrEmpty(token))
            {
                finalUrl = url.Contains("?")
                    ? ZString.Format("{0}&token={1}", url, token)
                    : ZString.Format("{0}?token={1}", url, token);
            }
            GameLaunch.ServerUrl = finalUrl;
            GameLaunch.UseRemoteSession = true;
            return ZString.Format("remote=ON url={0}", finalUrl);
        }

        /// <summary>接続先をローカル対戦 (LocalGameSession) に戻す。</summary>
        public static string SetLocal()
        {
            GameLaunch.UseRemoteSession = false;
            return "remote=OFF (local)";
        }

        /// <summary>卓設定を仕込んで InGame シーンへ直行する (Lobby の UI 操作を省略)。</summary>
        public static string StartInGame(int seatCount, int myStack, int smallBlind, int bigBlind)
        {
            GameLaunch.NextConfig = new LocalGameSessionConfig
            {
                SeatCount = seatCount,
                MySeat = 0,
                StartingStack = myStack,
                SmallBlind = smallBlind,
                BigBlind = bigBlind,
            };
            // AIデバッグ経由の起動はバイイン精算しない (無からチップが湧くのを防ぐ)
            GameLaunch.ChipsAtStake = false;
            _ = SceneController.Instance.LoadSceneWithFadeAsync(SceneId.InGame, SceneIdExtensions.ToSceneName);
            return ZString.Format("InGame へ遷移 (seats={0} stack={1} sb={2} bb={3} remote={4})",
                seatCount, myStack, smallBlind, bigBlind, GameLaunch.UseRemoteSession);
        }

        /// <summary>任意のシーンへ遷移する (SceneId の名前で指定。例: "Home", "Lobby", "Title")。</summary>
        public static string GoToScene(string sceneName)
        {
            if (!System.Enum.TryParse(sceneName, out SceneId sceneId))
            {
                return ZString.Format("不明なシーン: {0}", sceneName);
            }
            _ = SceneController.Instance.LoadSceneWithFadeAsync(sceneId, SceneIdExtensions.ToSceneName);
            return ZString.Format("{0} へ遷移", sceneName);
        }

        /// <summary>
        /// サーバー対戦を一発で開始する基盤ヘルパ (runCommand の往復を1回にまとめる)。
        /// Remote 切替 → AutoPlay 設定 → InGame 遷移 をまとめて行う。
        /// autoPlay=false なら手動 (Act で1手ずつ操作)。allIn=true は決着を早めるアグレッシブ AutoPlay。
        /// </summary>
        public static string Launch(string url, string token, int seatCount,
            bool autoPlay, bool skipEffects, bool allIn)
        {
            string r1 = SetRemote(url, token);
            string r2 = SetAutoPlay(autoPlay, skipEffects, allIn);
            string r3 = StartInGame(seatCount, 200, 1, 2);
            return ZString.Format("{0} | {1} | {2}", r1, r2, r3);
        }

        /// <summary>
        /// 現在の卓状態を AI が読める1行 JSON で返す。InGame にいないときは status を返す。
        /// presented=画面提示済みの状態 (入力判定と同じ)。演出中は presenting=true。
        /// </summary>
        public static string Snapshot()
        {
            KTC.Scene.InGameTable table = KTC.Scene.InGameTable.DebugActive;
            if (table == null)
            {
                return "{\"status\":\"not_in_game\"}";
            }
            TableStateMessage s = table.DebugPresentedState;
            if (s == null)
            {
                return "{\"status\":\"connecting\"}";
            }
            return BuildSnapshotJson(s, table.DebugIsPresenting);
        }

        /// <summary>
        /// アクションを送信する。type: 0=Fold 1=Check 2=Call 3=RaiseTo。amount は RaiseTo のみ。
        /// 手番でない/InGame でないときは送らず理由を返す。
        /// </summary>
        public static string Act(int type, int amount)
        {
            KTC.Scene.InGameTable table = KTC.Scene.InGameTable.DebugActive;
            if (table == null)
            {
                return "not_in_game";
            }
            TableStateMessage s = table.DebugPresentedState;
            if (s == null || !s.isYourTurn)
            {
                return "not_your_turn";
            }
            table.DebugSendAction(type, amount);
            return ZString.Format("sent type={0} amount={1}", type, amount);
        }

        /// <summary>次ハンドへ進む (ready)。ハンド終了後にのみ有効。</summary>
        public static string Ready()
        {
            KTC.Scene.InGameTable table = KTC.Scene.InGameTable.DebugActive;
            if (table == null)
            {
                return "not_in_game";
            }
            table.DebugSendReady();
            return "ready sent";
        }

        /// <summary>
        /// オートプレイを設定する。ON にすると自分の手番を自動 check/call し、
        /// ハンド完了で自動 ready する (InGameTable.Update のフックが駆動)。
        /// skipEffects=true で配布アニメ等の演出を飛ばし、進行を速める。
        /// </summary>
        public static string SetAutoPlay(bool enabled, bool skipEffects)
        {
            return SetAutoPlay(enabled, skipEffects, false);
        }

        /// <summary>
        /// オートプレイを設定する (allIn 版)。allIn=true で自分の手番は常にオールイン
        /// (決着を早めるアグレッシブモード。ゲーム終了までの確認に使う)。
        /// </summary>
        public static string SetAutoPlay(bool enabled, bool skipEffects, bool allIn)
        {
            DebugGameSettings.AutoPlay = enabled;
            DebugGameSettings.SkipEffects = skipEffects;
            DebugGameSettings.AutoPlayAllIn = allIn;
            return ZString.Format("autoPlay={0} skipEffects={1} allIn={2}", enabled, skipEffects, allIn);
        }

        // ---- JSON 組み立て (JsonUtility ではなく AI が読みやすい要約を自前で作る) ----

        private static string BuildSnapshotJson(TableStateMessage s, bool presenting)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("{");
            AppendInt(sb, "handNumber", s.handNumber);
            AppendInt(sb, "street", s.street);
            AppendInt(sb, "pot", s.pot);
            AppendInt(sb, "currentBet", s.currentBet);
            AppendInt(sb, "currentSeat", s.currentSeat);
            AppendInt(sb, "yourSeat", s.yourSeat);
            AppendBool(sb, "isYourTurn", s.isYourTurn);
            AppendBool(sb, "isComplete", s.isComplete);
            AppendBool(sb, "isGameOver", s.isGameOver);
            AppendBool(sb, "presenting", presenting);

            sb.Append("\"community\":");
            AppendCardArray(sb, s.communityCards);
            sb.Append(",");

            // 自分の手札
            sb.Append("\"yourHole\":");
            if (s.seats != null && s.yourSeat >= 0 && s.yourSeat < s.seats.Length)
            {
                AppendCardArray(sb, s.seats[s.yourSeat].holeCards);
            }
            else
            {
                sb.Append("[]");
            }
            sb.Append(",");

            // アクション要求 (自分の手番のとき有効)
            sb.Append("\"actionRequest\":{");
            AppendBool(sb, "canCheck", s.actionRequest.canCheck);
            AppendBool(sb, "canCall", s.actionRequest.canCall);
            AppendBool(sb, "canRaise", s.actionRequest.canRaise);
            AppendInt(sb, "callAmount", s.actionRequest.callAmount);
            AppendInt(sb, "minRaiseTo", s.actionRequest.minRaiseTo);
            AppendIntLast(sb, "maxRaiseTo", s.actionRequest.maxRaiseTo);
            sb.Append("},");

            // 席のスタック概要
            sb.Append("\"seats\":[");
            if (s.seats != null)
            {
                for (int i = 0; i < s.seats.Length; i++)
                {
                    SeatStateMessage seat = s.seats[i];
                    if (i > 0)
                    {
                        sb.Append(",");
                    }
                    sb.Append("{");
                    AppendInt(sb, "seat", seat.seat);
                    AppendInt(sb, "stack", seat.stack);
                    AppendInt(sb, "streetBet", seat.streetBet);
                    AppendBool(sb, "folded", seat.folded);
                    AppendBool(sb, "allIn", seat.allIn);
                    AppendBoolLast(sb, "sittingOut", seat.sittingOut);
                    sb.Append("}");
                }
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendCardArray(StringBuilder sb, byte[] cards)
        {
            sb.Append("[");
            if (cards != null)
            {
                for (int i = 0; i < cards.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(",");
                    }
                    // 数値と可読表記の両方を出す (例: "14:As")。0 は裏面。
                    Card card = Card.TryFromValue(cards[i], out Card c) ? c : Card.None;
                    sb.Append("\"");
                    sb.Append(cards[i]);
                    sb.Append(":");
                    sb.Append(card.IsNone ? "??" : card.ToString());
                    sb.Append("\"");
                }
            }
            sb.Append("]");
        }

        private static void AppendInt(StringBuilder sb, string key, int value)
        {
            sb.Append("\"").Append(key).Append("\":").Append(value).Append(",");
        }

        private static void AppendIntLast(StringBuilder sb, string key, int value)
        {
            sb.Append("\"").Append(key).Append("\":").Append(value);
        }

        private static void AppendBool(StringBuilder sb, string key, bool value)
        {
            sb.Append("\"").Append(key).Append("\":").Append(value ? "true" : "false").Append(",");
        }

        private static void AppendBoolLast(StringBuilder sb, string key, bool value)
        {
            sb.Append("\"").Append(key).Append("\":").Append(value ? "true" : "false");
        }
    }
}
#endif
