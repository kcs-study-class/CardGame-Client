using System;
using KTC.Poker.Protocol;
using R3;

namespace KTC.Poker.Session
{
    /// <summary>
    /// 「卓に着いている1人のプレイヤー」から見たゲームセッションの境界 (シーム)。
    ///
    /// UI はこのインターフェースだけを見て動く:
    /// - <see cref="StateUpdated"/> で受け取った <see cref="TableStateMessage"/> を描画する
    /// - 自分の手番 (isYourTurn) なら <see cref="SendAction"/> でアクションを送る
    /// - ハンド終了 (isComplete) を確認したら <see cref="SendReady"/> で次ハンドへ
    ///
    /// 通知は R3 の Observable で公開する (購読解除は Dispose か CancellationToken 連携)。
    ///
    /// 実装が2系統ある:
    /// - <see cref="LocalGameSession"/>: ローカル完結 (エンジン+Bot 内蔵)。リファレンス実装
    /// - RemoteGameSession (生徒課題): 同じメッセージを WebSocket で Go サーバーと送受信する
    /// </summary>
    public interface IGameSession : IDisposable
    {
        /// <summary>自分の卓上座席番号。</summary>
        int MySeatIndex { get; }

        bool IsConnected { get; }

        /// <summary>接続確立時に一度発火。</summary>
        Observable<Unit> Connected { get; }

        /// <summary>卓状態のスナップショット受信 (アクション1つごとに届く)。</summary>
        Observable<TableStateMessage> StateUpdated { get; }

        /// <summary>不正アクションや通信エラーの通知。UI はトースト表示などに使う。</summary>
        Observable<string> ErrorOccurred { get; }

        /// <summary>セッションを開始する。成功すると Connected → 初期 StateUpdated が届く。</summary>
        void Connect();

        /// <summary>自分の手番のアクションを送信する。</summary>
        void SendAction(PlayerActionMessage action);

        /// <summary>ハンド終了後、次のハンドへ進む準備完了を通知する。</summary>
        void SendReady();
    }
}
