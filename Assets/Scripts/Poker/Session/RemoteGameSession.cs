using System;
using KTC.Poker.Protocol;
using R3;

namespace KTC.Poker.Session
{
    /// <summary>
    /// Go サーバーと WebSocket で通信するゲームセッション (生徒課題)。
    ///
    /// 【課題】この骨組みを完成させて、サーバーと対戦できるようにする。
    /// 仕様: CardGame-Server/Docs/API.md (WebSocket プロトコル) と Docs/ASSIGNMENTS.md (進め方)。
    /// 完成見本 (サーバーが返すべき内容): <see cref="LocalGameSession"/>。
    ///
    /// 実装の指針:
    /// 1. <see cref="Connect"/> で System.Net.WebSockets.ClientWebSocket を使い
    ///    <see cref="_serverUrl"/> (例 ws://localhost:8080/ws) へ接続する
    /// 2. 送信: <see cref="SendAction"/>/<see cref="SendReady"/> を
    ///    GameMessageEnvelope {type, payload} の JSON にして SendAsync する
    /// 3. 受信: ReceiveAsync のループを回し、envelope.type == "tableState" なら
    ///    payload を TableStateMessage にデシリアライズして <see cref="StateUpdated"/> に流す
    /// 4. 受信ループはバックグラウンドスレッドで動くため、Subject への OnNext は
    ///    R3.Unity の ObserveOnMainThread() 等でメインスレッドへ配送すること
    ///    (UI は Unity API を触るので、別スレッドから流すと例外や無反応になる)
    /// 5. 切断・例外は <see cref="ErrorOccurred"/> へ流す (UI 側の表示は実装済み)
    /// </summary>
    public sealed class RemoteGameSession : IGameSession
    {
        private readonly string _serverUrl;

        private readonly Subject<Unit> _connected = new Subject<Unit>();
        private readonly Subject<TableStateMessage> _stateUpdated = new Subject<TableStateMessage>();
        private readonly Subject<string> _errorOccurred = new Subject<string>();

        public RemoteGameSession(string serverUrl)
        {
            _serverUrl = serverUrl;
        }

        public int MySeatIndex { get; private set; }

        public bool IsConnected { get; private set; }

        public Observable<Unit> Connected => _connected;
        public Observable<TableStateMessage> StateUpdated => _stateUpdated;
        public Observable<string> ErrorOccurred => _errorOccurred;

        public void Connect()
        {
            // TODO(生徒課題/クライアント班): ClientWebSocket で _serverUrl へ接続し、
            // 受信ループを開始する。接続成功で IsConnected=true と _connected.OnNext(Unit.Default)
            _errorOccurred.OnNext("RemoteGameSession は未実装です (生徒課題)。デバッグメニューで接続先をローカルに戻してください。");
        }

        public void SendAction(PlayerActionMessage action)
        {
            // TODO(生徒課題/クライアント班): envelope {type:"playerAction", payload:JSON} を送信する
        }

        public void SendReady()
        {
            // TODO(生徒課題/クライアント班): envelope {type:"ready", payload:""} を送信する
        }

        public void Dispose()
        {
            // TODO(生徒課題/クライアント班): 受信ループの停止と WebSocket のクローズ
            _connected.Dispose();
            _stateUpdated.Dispose();
            _errorOccurred.Dispose();
        }
    }
}
