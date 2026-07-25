using KTC.Poker.Protocol;

namespace KTC.Poker.Session
{
    /// <summary>
    /// ローカル対戦相手 (Bot) の思考ルーチン。
    /// 受け取る情報はプレイヤーと同じ「リダクション済みスナップショット」なので、
    /// Bot は他人のホールカードを盗み見できない (カンニング構造的に不可)。
    /// 生徒の拡張課題: これを実装して強い Bot を作る。
    /// </summary>
    public interface IBotPolicy
    {
        /// <param name="view">Bot 自身の視点でリダクションされた卓状態。</param>
        /// <param name="legal">選択可能なアクション。</param>
        /// <returns>実行するアクション。不正な内容はフォールドとして扱われる。</returns>
        PlayerActionMessage Decide(TableStateMessage view, ActionRequestMessage legal);
    }

    /// <summary>
    /// 最小の Bot: チェックできればチェック、できなければコール (いわゆるコーリングステーション)。
    /// 決定論的なのでテスト・デバッグの既定 Bot として使う。
    /// </summary>
    public sealed class CallingBot : IBotPolicy
    {
        public PlayerActionMessage Decide(TableStateMessage view, ActionRequestMessage legal)
        {
            return new PlayerActionMessage
            {
                actionType = legal.canCheck ? 1 : 2, // Check : Call
                amount = 0,
            };
        }
    }
}
