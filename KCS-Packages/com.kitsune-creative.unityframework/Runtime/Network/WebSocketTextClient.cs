using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnityFramework.Network
{
    /// <summary>
    /// テキストメッセージ用の汎用 WebSocket クライアント。
    ///
    /// - 送受信は ThreadPool (async/await) で行う
    /// - イベントは生成時に捕まえたメインスレッドの SynchronizationContext へ Post して発火する
    ///   (購読側は Unity API を安全に触れる)。メインスレッドで生成すること
    /// - 送信は内部で直列化される (SendAsync の同時呼び出し禁止制約を吸収)
    ///
    /// プロトコル (メッセージの中身の解釈) は呼び出し側の責務。
    /// </summary>
    public sealed class WebSocketTextClient : IDisposable
    {
        private readonly string _url;
        private readonly SynchronizationContext _mainThread;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _socket = null;
        private bool _disposed = false;

        /// <summary>接続確立時 (メインスレッド)。</summary>
        public event Action Connected;

        /// <summary>テキストメッセージ受信時 (メインスレッド)。</summary>
        public event Action<string> MessageReceived;

        /// <summary>接続失敗・切断・送受信エラー時 (メインスレッド)。</summary>
        public event Action<string> ErrorOccurred;

        public WebSocketTextClient(string url)
        {
            _url = url;
            // メインスレッドで生成される前提
            _mainThread = SynchronizationContext.Current;
        }

        public bool IsOpen => _socket != null && _socket.State == WebSocketState.Open;

        /// <summary>接続を開始する。結果は Connected / ErrorOccurred で通知される。</summary>
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

        /// <summary>テキストメッセージを送信する (fire-and-forget、内部で直列化)。</summary>
        public void Send(string text)
        {
            if (_disposed || !IsOpen)
            {
                PostError("接続されていません。");
                return;
            }
            _ = SendAsync(Encoding.UTF8.GetBytes(text));
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
        }

        // ---- 内部処理 ----

        private async Task RunAsync()
        {
            try
            {
                await _socket.ConnectAsync(new Uri(_url), _cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PostError($"サーバーに接続できません: {e.Message}");
                return;
            }
            PostToMain(() => Connected?.Invoke());
            await ReceiveLoopAsync().ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[64 * 1024];
            try
            {
                while (!_cts.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    using (MemoryStream stream = new MemoryStream())
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

                        string text = Encoding.UTF8.GetString(stream.ToArray());
                        PostToMain(() => MessageReceived?.Invoke(text));
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
                    PostError($"受信エラー: {e.Message}");
                }
            }
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
                    PostError($"送信エラー: {e.Message}");
                }
            }
        }

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
            PostToMain(() => ErrorOccurred?.Invoke(message));
        }
    }
}
