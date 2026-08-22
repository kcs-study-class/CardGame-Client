using KTC.Poker.Protocol;
using R3;
using UnityEngine;
using UnityFramework.Network;

namespace KTC.Poker.Session
{
    /// <summary>
    /// Go サーバーと WebSocket で通信する <see cref="IGameSession"/> 実装 (リファレンス)。
    /// 仕様: CardGame-Server/Docs/API.md。サーバー側の対は internal/ws + internal/table。
    ///
    /// ソケットの接続・受信ループ・直列送信・メインスレッド配送は
    /// <see cref="WebSocketTextClient"/> (framework) が担い、このクラスは
    /// プロトコル (封筒の解釈と IGameSession への写像) だけを実装する。
    ///
    /// v1 の制約: 卓設定 (座席数など) はサーバー側の設定が正。クライアントの Lobby 選択は
    /// ローカル対戦にのみ効く (サーバーの SEAT_COUNT 等と合わせておくこと)。
    /// </summary>
    public sealed class RemoteGameSession : IGameSession
    {
        private readonly WebSocketTextClient _client;
        private bool _disposed;

        private readonly Subject<Unit> _connected = new Subject<Unit>();
        private readonly Subject<TableStateMessage> _stateUpdated = new Subject<TableStateMessage>();
        private readonly Subject<string> _errorOccurred = new Subject<string>();

        public RemoteGameSession(string serverUrl)
        {
            _client = new WebSocketTextClient(serverUrl);
            // WebSocketTextClient のイベントはメインスレッドで発火する
            _client.MessageReceived += HandleMessage;
            _client.ErrorOccurred += message =>
            {
                if (!_disposed)
                {
                    _errorOccurred.OnNext(message);
                }
            };
        }

        public int MySeatIndex { get; private set; }

        public bool IsConnected { get; private set; }

        public Observable<Unit> Connected => _connected;
        public Observable<TableStateMessage> StateUpdated => _stateUpdated;
        public Observable<string> ErrorOccurred => _errorOccurred;

        public void Connect()
        {
            _client.Connect();
        }

        public void SendAction(PlayerActionMessage action)
        {
            SendEnvelope(MessageTypes.PLAYER_ACTION, JsonUtility.ToJson(action));
        }

        public void SendReady()
        {
            SendEnvelope(MessageTypes.READY, "{}");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _client.Dispose();
            _connected.Dispose();
            _stateUpdated.Dispose();
            _errorOccurred.Dispose();
        }

        // ---- プロトコル ----

        private void SendEnvelope(string type, string payload)
        {
            _client.Send(JsonUtility.ToJson(new GameMessageEnvelope { type = type, payload = payload }));
        }

        private void HandleMessage(string json)
        {
            if (_disposed)
            {
                return;
            }
            var envelope = JsonUtility.FromJson<GameMessageEnvelope>(json);
            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                return;
            }

            switch (envelope.type)
            {
                case MessageTypes.JOIN_ACK:
                    var ack = JsonUtility.FromJson<JoinAckMessage>(envelope.payload);
                    MySeatIndex = ack.yourSeat;
                    IsConnected = true;
                    _connected.OnNext(Unit.Default);
                    break;

                case MessageTypes.TABLE_STATE:
                    _stateUpdated.OnNext(JsonUtility.FromJson<TableStateMessage>(envelope.payload));
                    break;

                case MessageTypes.ERROR:
                    var error = JsonUtility.FromJson<ErrorMessage>(envelope.payload);
                    _errorOccurred.OnNext(error.message);
                    break;
            }
        }
    }
}
