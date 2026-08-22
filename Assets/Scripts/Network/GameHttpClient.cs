using System;
using System.Net.Http;
using Cysharp.Net.Http;

/// <summary>
/// ゲーム全体で共有する HttpClient (YetAnotherHttpHandler = HTTP/2 対応)。
/// 生徒課題の API 呼び出し (ログイン / お知らせ / メンテ確認 — CardGame-Server/Docs/API.md §2)
/// はこれを使う。使用例:
/// <code>
/// var response = await GameHttpClient.Shared.PostAsync("/api/v1/login", content, ct);
/// </code>
/// </summary>
public static class GameHttpClient
{
    // FIXME : 接続先はサーバー実装時に確定 (docker-compose の api ポートに合わせる)
    public const string BASE_ADDRESS = "http://localhost:8081";

    private static HttpClient SharedInstance;

    public static HttpClient Shared
    {
        get
        {
            if (SharedInstance == null)
            {
                SharedInstance = new HttpClient(new YetAnotherHttpHandler())
                {
                    BaseAddress = new Uri(BASE_ADDRESS),
                    Timeout = TimeSpan.FromSeconds(10),
                };
            }
            return SharedInstance;
        }
    }
}
