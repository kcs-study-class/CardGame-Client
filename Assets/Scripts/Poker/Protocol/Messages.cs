using System;

namespace KTC.Poker.Protocol
{
    // =====================================================================
    // クライアント⇔サーバー間のメッセージ定義 (プロトコル仕様)。
    //
    // - ローカルプレイでは LocalGameSession がこの DTO をそのまま渡す。
    //   サーバー接続時は同じ DTO を JSON にして WebSocket で送受信する。
    //   つまりこのファイルが Go サーバーの実装仕様書を兼ねる。
    // - JsonUtility 制約に合わせた設計:
    //     * public フィールドのみ / [Serializable] クラス
    //     * Dictionary・null ネスト不可 → ネストは常に非 null + bool フラグで有効判定
    //     * enum は int で持つ (Go 側は数値で扱う)
    // - カードは 1 byte のパック値 (KTC.Poker.Domain.Card.Value)。0 = 未公開 (裏面)。
    //   他プレイヤーのホールカードはサーバー側で 0 に伏せて送る (リダクション)。
    // =====================================================================

    /// <summary>WebSocket で送受信する外側の封筒。payload は各メッセージの JSON 文字列。</summary>
    [Serializable]
    public class GameMessageEnvelope
    {
        public string type;
        public string payload;
    }

    /// <summary>メッセージ種別 (envelope.type の値)。</summary>
    public static class MessageTypes
    {
        // サーバー → クライアント
        public const string TableState = "tableState";
        // クライアント → サーバー
        public const string PlayerAction = "playerAction";
        public const string Ready = "ready";
    }

    /// <summary>
    /// 卓の全量スナップショット。サーバーは状態が変わるたびに (アクション1つごとに)
    /// 受信者向けにリダクションしたこのメッセージを配信する。
    /// クライアントはこれを受け取って画面を丸ごと再描画すればよい (差分管理不要)。
    /// </summary>
    [Serializable]
    public class TableStateMessage
    {
        public int handNumber;
        /// <summary>KTC.Poker.Domain.Street の int 値 (0=Preflop .. 4=Showdown)。</summary>
        public int street;
        /// <summary>公開済みコミュニティカードのみ (0〜5枚)。</summary>
        public byte[] communityCards;
        public int pot;
        public int currentBet;
        /// <summary>手番の卓上座席。なければ -1。</summary>
        public int currentSeat;
        public int buttonSeat;
        public int smallBlindSeat;
        public int bigBlindSeat;
        /// <summary>このメッセージの受信者の座席。</summary>
        public int yourSeat;
        public SeatStateMessage[] seats;

        /// <summary>true のとき actionRequest が有効 (あなたの手番)。</summary>
        public bool isYourTurn;
        public ActionRequestMessage actionRequest = new ActionRequestMessage();

        /// <summary>true のときハンド終了済みで result が有効。</summary>
        public bool isComplete;
        public HandResultMessage result = new HandResultMessage();

        /// <summary>チップを持つ席が1つ以下になり、卓が終了した。</summary>
        public bool isGameOver;
    }

    [Serializable]
    public class SeatStateMessage
    {
        public int seat;
        public int stack;
        public int streetBet;
        public int totalCommitted;
        public bool folded;
        public bool allIn;
        /// <summary>チップが尽きて参加していない席。</summary>
        public bool sittingOut;
        /// <summary>ホールカード2枚。非公開は 0 (裏面)。未配布・観戦席は [0,0]。</summary>
        public byte[] holeCards;
    }

    /// <summary>あなたの手番で選択可能なアクション (TableStateMessage.isYourTurn=true のとき有効)。</summary>
    [Serializable]
    public class ActionRequestMessage
    {
        public bool canCheck;
        public bool canCall;
        public bool canRaise;
        /// <summary>コールに必要な追加チップ。</summary>
        public int callAmount;
        /// <summary>最小レイズ後の合計ベット額。</summary>
        public int minRaiseTo;
        /// <summary>最大レイズ後の合計ベット額 (=オールイン)。</summary>
        public int maxRaiseTo;
    }

    /// <summary>クライアント → サーバー: 自分のアクション。</summary>
    [Serializable]
    public class PlayerActionMessage
    {
        /// <summary>KTC.Poker.Domain.ActionType の int 値 (0=Fold, 1=Check, 2=Call, 3=RaiseTo)。</summary>
        public int actionType;
        /// <summary>RaiseTo のみ使用: そのストリートの合計ベット額。</summary>
        public int amount;
    }

    /// <summary>ハンド終了時の結果 (TableStateMessage.isComplete=true のとき有効)。</summary>
    [Serializable]
    public class HandResultMessage
    {
        public bool wentToShowdown;
        public PotResultMessage[] pots;
        /// <summary>卓上座席ごとの獲得額 (座席数ぶんの配列)。</summary>
        public int[] payouts;
        /// <summary>ショーダウン参加者の公開情報 (フォールド決着時は空)。</summary>
        public ShowdownHandMessage[] showdownHands;
    }

    [Serializable]
    public class PotResultMessage
    {
        public int amount;
        public int[] eligibleSeats;
        public int[] winnerSeats;
    }

    [Serializable]
    public class ShowdownHandMessage
    {
        public int seat;
        /// <summary>KTC.Poker.Domain.HandCategory の int 値。</summary>
        public int category;
        /// <summary>公開されたホールカード。</summary>
        public byte[] holeCards;
    }
}
