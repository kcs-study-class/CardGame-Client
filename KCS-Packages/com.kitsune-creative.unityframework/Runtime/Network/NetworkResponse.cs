namespace UnityFramework.Network
{
    /// <summary>
    /// ネットワークリクエストの結果ステータス。
    /// </summary>
    public enum NetworkResponseStatus
    {
        /// <summary>2xx 系で正常にデシリアライズも完了。</summary>
        Success,
        /// <summary>4xx/5xx 等の HTTP プロトコルエラー。</summary>
        ProtocolError,
        /// <summary>接続失敗・DNS解決失敗・タイムアウト等の通信エラー。</summary>
        ConnectionError,
        /// <summary>レスポンスは受け取ったがデシリアライズに失敗。</summary>
        DataProcessingError,
        /// <summary>CancellationToken によりキャンセルされた。</summary>
        Cancelled,
    }

    /// <summary>
    /// ネットワークリクエストのレスポンスをラップする値型。
    /// 例外を投げずに成否を呼び出し側で判定できるようにする。
    /// </summary>
    public readonly struct NetworkResponse<T>
    {
        public NetworkResponseStatus Status { get; }
        public T Data { get; }
        public long StatusCode { get; }
        public string ErrorMessage { get; }
        public string RawBody { get; }

        public bool IsSuccess => Status == NetworkResponseStatus.Success;

        private NetworkResponse(NetworkResponseStatus status, T data, long statusCode, string errorMessage, string rawBody)
        {
            Status = status;
            Data = data;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            RawBody = rawBody;
        }

        public static NetworkResponse<T> Success(T data, long statusCode, string rawBody)
            => new NetworkResponse<T>(NetworkResponseStatus.Success, data, statusCode, null, rawBody);

        public static NetworkResponse<T> Error(NetworkResponseStatus status, string errorMessage, long statusCode = 0, string rawBody = null)
            => new NetworkResponse<T>(status, default, statusCode, errorMessage, rawBody);

        public static NetworkResponse<T> Cancelled()
            => new NetworkResponse<T>(NetworkResponseStatus.Cancelled, default, 0, "Request was cancelled.", null);
    }
}
