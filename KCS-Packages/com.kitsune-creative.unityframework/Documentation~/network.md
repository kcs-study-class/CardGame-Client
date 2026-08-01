# ネットワーク

名前空間: `UnityFramework.Network`

`UnityWebRequest` ベースの HTTP REST API クライアント基底クラス。

## 基本構造

API ごとに `NetworkControllerBase` を継承して `BaseUrl` を実装します。

```csharp
using UnityFramework.Network;

public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";
}
```

## リクエスト

```csharp
var client = new MyApiClient();

// GET
var res = await client.GetAsync<UserData>("/user/me");

// POST
var loginReq = new LoginRequest { id = "...", pass = "..." };
var loginRes = await client.PostAsync<LoginRequest, LoginResponse>("/login", loginReq);

// PUT
await client.PutAsync<UpdateRequest, UserData>("/user/me", updateReq);

// DELETE
await client.DeleteAsync<EmptyResponse>("/session");
```

## レスポンスハンドリング

戻り値は `NetworkResponse<T>` (構造体)。例外ではなく値で成否を判定。

```csharp
if (res.IsSuccess)
{
    Use(res.Data);
    Debug.Log($"Status: {res.StatusCode}");
}
else
{
    switch (res.Status)
    {
        case NetworkResponseStatus.ProtocolError:        // 4xx/5xx
            Debug.LogError($"[{res.StatusCode}] {res.ErrorMessage}");
            break;
        case NetworkResponseStatus.ConnectionError:      // 接続失敗・タイムアウト
            Debug.LogWarning($"通信失敗: {res.ErrorMessage}");
            break;
        case NetworkResponseStatus.DataProcessingError:  // デシリアライズ失敗
            Debug.LogError($"レスポンス解析失敗: {res.ErrorMessage}");
            Debug.Log($"Raw: {res.RawBody}");
            break;
        case NetworkResponseStatus.Cancelled:            // キャンセル
            Debug.Log("ユーザーキャンセル");
            break;
    }
}
```

### NetworkResponse\<T\> プロパティ

| プロパティ | 説明 |
|---|---|
| `IsSuccess` | `Status == Success` |
| `Status` | 5値の `NetworkResponseStatus` enum |
| `Data` | デシリアライズ済みデータ (失敗時 default) |
| `StatusCode` | HTTP ステータスコード |
| `ErrorMessage` | エラー詳細 |
| `RawBody` | 生レスポンス本文 |

## ヘッダ / 認証

### 静的ヘッダ

```csharp
public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";

    protected override IReadOnlyDictionary<string, string> DefaultHeaders => new Dictionary<string, string>
    {
        ["X-App-Version"] = Application.version,
        ["Accept-Language"] = "ja",
    };
}
```

### 動的ヘッダ (認証トークン等)

```csharp
public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";

    protected override void OnBeforeRequest(UnityWebRequest request)
    {
        var token = AuthService.CurrentToken;
        if (!string.IsNullOrEmpty(token))
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
        }
    }
}
```

## カスタムシリアライズ

既定では `JsonUtility` を使用。Newtonsoft.Json 等に差し替え可能。

```csharp
using Newtonsoft.Json;

public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";

    protected override string Serialize<T>(T obj) => JsonConvert.SerializeObject(obj);
    protected override T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);
}
```

## タイムアウト / キャンセル

```csharp
public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";
    protected override int TimeoutSeconds => 60;       // 既定30秒、0以下で無期限
}

// キャンセル
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(10));

var res = await client.GetAsync<UserData>("/user/me", cts.Token);
if (res.Status == NetworkResponseStatus.Cancelled)
{
    // キャンセル処理
}
```

## 任意 HTTP メソッド

PATCH や HEAD 等を使いたい場合は `SendAsync` を直接呼び出します (`protected`)。

```csharp
public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";

    public Awaitable<NetworkResponse<UserData>> PatchUserAsync(UpdateRequest req, CancellationToken ct = default)
    {
        return SendAsync<UserData>("PATCH", "/user", Serialize(req), ct);
    }
}
```

## 文字列レスポンス

`TResponse = string` を指定するとデシリアライズせず生レスポンスを取得できます (テキスト/CSV/XML 等の用途)。

```csharp
var res = await client.GetAsync<string>("/raw-text");
Debug.Log(res.Data);
```

## URL 解決ルール

- パスが `http://` / `https://` で始まる → 絶対 URL として扱う
- それ以外 → `BaseUrl + path` を結合 (末尾/先頭スラッシュは自動正規化)
- `BaseUrl` が空 → パスをそのまま使用
