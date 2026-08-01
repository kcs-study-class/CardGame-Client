using System;

namespace KTC.Poker.Domain
{
    /// <summary>ハンド内の進行段階。</summary>
    public enum Street
    {
        Preflop = 0,
        Flop = 1,
        Turn = 2,
        River = 3,
        Showdown = 4,
    }

    /// <summary>プレイヤーアクションの種類。通信でもこの値を使う。</summary>
    public enum ActionType : byte
    {
        Fold = 0,
        Check = 1,
        Call = 2,
        /// <summary>ベット/レイズ。Amount は「そのストリートの合計投入額」(上乗せ額ではない)。</summary>
        RaiseTo = 3,
    }

    /// <summary>
    /// プレイヤーの1アクション。RaiseTo のみ Amount を使う。
    /// Amount は「そのストリートでの自分の合計ベット額」で指定する
    /// (例: BB=2 に対して 6 にレイズ → RaiseTo(6))。
    /// </summary>
    public readonly struct PlayerAction
    {
        public ActionType Type { get; }
        public int Amount { get; }

        private PlayerAction(ActionType type, int amount)
        {
            Type = type;
            Amount = amount;
        }

        public static PlayerAction Fold() => new PlayerAction(ActionType.Fold, 0);
        public static PlayerAction Check() => new PlayerAction(ActionType.Check, 0);
        public static PlayerAction Call() => new PlayerAction(ActionType.Call, 0);

        public static PlayerAction RaiseTo(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "RaiseTo の額は正の値で指定してください。");
            }
            return new PlayerAction(ActionType.RaiseTo, amount);
        }

        public override string ToString()
        {
            return Type == ActionType.RaiseTo ? $"RaiseTo({Amount})" : Type.ToString();
        }
    }

    /// <summary>
    /// 現在の手番プレイヤーが取れるアクションの一覧。UI とバリデーションの共通ソース。
    /// </summary>
    public sealed class LegalActions
    {
        public int SeatIndex { get; internal set; }
        public bool CanFold { get; internal set; }
        public bool CanCheck { get; internal set; }
        public bool CanCall { get; internal set; }
        /// <summary>コールに必要な追加チップ (スタック上限でクリップ済み)。</summary>
        public int CallAmount { get; internal set; }
        public bool CanRaise { get; internal set; }
        /// <summary>最小レイズ後の合計ベット額 (オールインしか出来ない場合はその額)。</summary>
        public int MinRaiseTo { get; internal set; }
        /// <summary>最大レイズ後の合計ベット額 (= オールイン)。</summary>
        public int MaxRaiseTo { get; internal set; }
    }
}
