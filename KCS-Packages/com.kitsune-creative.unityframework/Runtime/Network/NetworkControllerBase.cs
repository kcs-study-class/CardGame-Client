using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityFramework.Network
{
    /// <summary>
    /// HTTP REST API クライアントの基底クラス。
    /// <list type="bullet">
    /// <item><c>BaseUrl</c> をオーバーライドして接続先を指定。</item>
    /// <item><c>DefaultHeaders</c> / <c>OnBeforeRequest</c> で共通ヘッダや認証を制御。</item>
    /// <item><c>Serialize</c> / <c>Deserialize</c> をオーバーライドすれば JSON 以外や Newtonsoft 等への差し替えが可能。</item>
    /// </list>
    /// レスポンスは例外ではなく <see cref="NetworkResponse{T}"/> で返す設計。
    /// </summary>
    public abstract class NetworkControllerBase
    {
        /// <summary>API のベース URL。末尾スラッシュは任意。</summary>
        protected abstract string BaseUrl { get; }

        /// <summary>リクエストのタイムアウト秒数。0 以下で無期限。</summary>
        protected virtual int TimeoutSeconds => 30;

        /// <summary>全リクエストに付与するヘッダ。動的な値は <see cref="OnBeforeRequest"/> で設定する。</summary>
        protected virtual IReadOnlyDictionary<string, string> DefaultHeaders => null;

        /// <summary>
        /// リクエストオブジェクトを保持する Awaitable のクラスインスタンス。
        /// 認証トークンの更新等、リクエスト直前に処理を挿入したい場合にオーバーライドする。
        /// </summary>
        protected virtual void OnBeforeRequest(UnityWebRequest request) { }

        protected virtual string Serialize<T>(T obj) => JsonUtility.ToJson(obj);
        protected virtual T Deserialize<T>(string json) => JsonUtility.FromJson<T>(json);

        public Awaitable<NetworkResponse<TResponse>> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default)
            => SendAsync<TResponse>(UnityWebRequest.kHttpVerbGET, path, null, cancellationToken);

        public Awaitable<NetworkResponse<TResponse>> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default)
            => SendAsync<TResponse>(UnityWebRequest.kHttpVerbPOST, path, Serialize(body), cancellationToken);

        public Awaitable<NetworkResponse<TResponse>> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default)
            => SendAsync<TResponse>(UnityWebRequest.kHttpVerbPUT, path, Serialize(body), cancellationToken);

        public Awaitable<NetworkResponse<TResponse>> DeleteAsync<TResponse>(string path, CancellationToken cancellationToken = default)
            => SendAsync<TResponse>(UnityWebRequest.kHttpVerbDELETE, path, null, cancellationToken);

        /// <summary>
        /// 任意メソッドのリクエストを送信する。標準の Get/Post/Put/Delete では足りない場合に直接使う。
        /// </summary>
        protected async Awaitable<NetworkResponse<TResponse>> SendAsync<TResponse>(string method, string path, string jsonBody, CancellationToken cancellationToken)
        {
            var url = BuildUrl(path);

            using var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds > 0 ? TimeoutSeconds : 0,
            };

            if (!string.IsNullOrEmpty(jsonBody))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            }

            ApplyDefaultHeaders(request);

            try
            {
                OnBeforeRequest(request);
            }
            catch (Exception ex)
            {
                return NetworkResponse<TResponse>.Error(NetworkResponseStatus.ConnectionError, $"OnBeforeRequest で例外: {ex.Message}");
            }

            try
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        return NetworkResponse<TResponse>.Cancelled();
                    }
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                request.Abort();
                return NetworkResponse<TResponse>.Cancelled();
            }
            catch (Exception ex)
            {
                return NetworkResponse<TResponse>.Error(NetworkResponseStatus.ConnectionError, ex.Message);
            }

            return ProcessResponse<TResponse>(request);
        }

        private string BuildUrl(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BaseUrl ?? string.Empty;
            }
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            if (string.IsNullOrEmpty(BaseUrl))
            {
                return path;
            }
            return $"{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        private void ApplyDefaultHeaders(UnityWebRequest request)
        {
            var headers = DefaultHeaders;
            if (headers == null) return;
            foreach (var kv in headers)
            {
                request.SetRequestHeader(kv.Key, kv.Value);
            }
        }

        private NetworkResponse<TResponse> ProcessResponse<TResponse>(UnityWebRequest request)
        {
            var statusCode = request.responseCode;
            var rawBody = request.downloadHandler?.text;

            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    try
                    {
                        // string 型をそのまま受け取りたいケースもある
                        if (typeof(TResponse) == typeof(string))
                        {
                            return NetworkResponse<TResponse>.Success((TResponse)(object)rawBody, statusCode, rawBody);
                        }

                        if (string.IsNullOrEmpty(rawBody))
                        {
                            return NetworkResponse<TResponse>.Success(default, statusCode, rawBody);
                        }

                        var data = Deserialize<TResponse>(rawBody);
                        return NetworkResponse<TResponse>.Success(data, statusCode, rawBody);
                    }
                    catch (Exception ex)
                    {
                        return NetworkResponse<TResponse>.Error(NetworkResponseStatus.DataProcessingError, ex.Message, statusCode, rawBody);
                    }

                case UnityWebRequest.Result.ProtocolError:
                    return NetworkResponse<TResponse>.Error(NetworkResponseStatus.ProtocolError, request.error, statusCode, rawBody);

                case UnityWebRequest.Result.ConnectionError:
                    return NetworkResponse<TResponse>.Error(NetworkResponseStatus.ConnectionError, request.error, statusCode, rawBody);

                case UnityWebRequest.Result.DataProcessingError:
                    return NetworkResponse<TResponse>.Error(NetworkResponseStatus.DataProcessingError, request.error, statusCode, rawBody);

                default:
                    return NetworkResponse<TResponse>.Error(NetworkResponseStatus.ConnectionError, request.error ?? "Unknown error", statusCode, rawBody);
            }
        }
    }
}
