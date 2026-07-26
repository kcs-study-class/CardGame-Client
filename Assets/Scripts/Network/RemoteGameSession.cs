using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Text;
using KTC.Poker.Protocol;
using R3;
using UnityEngine;

namespace KTC.Poker.Session
{
    /// <summary>
    /// Go サーバーと WebSocket で通信する <see cref="IGameSession"/> 実装 (リファレンス)。
    /// 仕様: CardGame-Server/Docs/API.md。サーバー側の対は internal/ws + internal/table。
    ///
    /// スレッドモデル:
    /// - 送受信は ThreadPool (async/await) で行う
    /// - Subject への通知は、生成時に捕まえたメインスレッドの SynchronizationContext へ
    ///   Post して行う (UI は Unity API を触るため、別スレッドから流してはいけない)
    ///
    /// v1 の制約: 卓設定 (座席数など) はサーバー側の設定が正。クライアントの Lobby 選択は
    /// ローカル対戦にのみ効く (サーバーの SEAT_COUNT 等と合わせておくこと)。
    /// </summary>
    public sealed class RemoteGameSession : IGameSession
    {
        private readonly string _serverUrl;
        private readonly SynchronizationContext _mainThread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket;
        private bool _disposed;

        private readonly Subject<Unit> _connected = new Subject<Unit>();
        private readonly Subject<TableStateMessage> _stateUpdated = new Subject<TableStateMessage>();
        private readonly Subject<string> _errorOccurred = new Subject<string>();

        public RemoteGameSession(string serverUrl)
        {
            _serverUrl = serverUrl;
            // メインスレッドで生成される前提 (InGameTable.PrepareAsync から呼ばれる)
            _mainThread = SynchronizationContext.Current;
        }

        public int MySeatIndex { get; private set; }

        public bool IsConnected { get; private set; }

        public Observable<Unit> Connected => _connected;
        public Observable<TableStateMessage> StateUpdated => _stateUpdated;
        public Observable<string> ErrorOccurred => _errorOccurred;

        public void Connect()
        {
            if (_disposed || _socket != null)
            {
                PostError("すでに接続済みか破棄されています。");
                return;
            }
            _socket = new ClientWebSocket();
            _ = RunAsync();
        }

        public void SendAction(PlayerActionMessage action)
        {
            Send(MessageTypes.PlayerAction, JsonUtility.ToJson(action));
        }

        public void SendReady()
        {
            Send(MessageTypes.Ready, "{}");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _cts.Cancel();
            try
            {
                _socket?.Abort();
            }
            catch
            {
                // クローズ中の例外は握りつぶしてよい (以降使わない)
            }
            _socket?.Dispose();
            _connected.Dispose();
            _stateUpdated.Dispose();
            _errorOccurred.Dispose();
        }

        // ---- 接続と受信 ----

        private async Task RunAsync()
        {
            try
            {
                await _socket.ConnectAsync(new Uri(_serverUrl), _cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PostError(ZString.Format("サーバーに接続できません: {0}", e.Message));
                return;
            }
            await ReceiveLoopAsync().ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    using (var stream = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token)
                                .ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                PostError("サーバーから切断されました。");
                                return;
                            }
                            stream.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        HandleMessage(Encoding.UTF8.GetString(stream.ToArray()));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose による停止
            }
            catch (Exception e)
            {
                if (!_disposed)
                {
                    PostError(ZString.Format("受信エラー: {0}", e.Message));
                }
            }
        }

        /// <summary>受信メッセージの解釈。JsonUtility はスレッドセーフなのでバックグラウンドのまま使える。</summary>
        private void HandleMessage(string json)
        {
            var envelope = JsonUtility.FromJson<GameMessageEnvelope>(json);
            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                return;
            }

            switch (envelope.type)
            {
                case MessageTypes.JoinAck:
                    var ack = JsonUtility.FromJson<JoinAckMessage>(envelope.payload);
                    PostToMain(() =>
                    {
                        MySeatIndex = ack.yourSeat;
                        IsConnected = true;
                        _connected.OnNext(Unit.Default);
                    });
                    break;

                case MessageTypes.TableState:
                    var state = JsonUtility.FromJson<TableStateMessage>(envelope.payload);
                    PostToMain(() => _stateUpdated.OnNext(state));
                    break;

                case MessageTypes.Error:
                    var error = JsonUtility.FromJson<ErrorMessage>(envelope.payload);
                    PostError(error.message);
                    break;
            }
        }

        // ---- 送信 ----

        private void Send(string type, string payload)
        {
            if (_disposed || _socket == null || _socket.State != WebSocketState.Open)
            {
                PostError("接続されていません。");
                return;
            }
            var envelope = new GameMessageEnvelope { type = type, payload = payload };
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope));
            _ = SendAsync(bytes);
        }

        private async Task SendAsync(byte[] bytes)
        {
            try
            {
                await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    await _socket.SendAsync(
                            new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, _cts.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose による停止
            }
            catch (Exception e)
            {
                if (!_disposed)
                {
                    PostError(ZString.Format("送信エラー: {0}", e.Message));
                }
            }
        }

        // ---- メインスレッド配送 ----

        private void PostToMain(Action action)
        {
            if (_mainThread == null)
            {
                action(); // テスト等、メインスレッド文脈がない場合のフォールバック
                return;
            }
            _mainThread.Post(_ =>
            {
                if (!_disposed)
                {
                    action();
                }
            }, null);
        }

        private void PostError(string message)
        {
            PostToMain(() => _errorOccurred.OnNext(message));
        }
    }
}
