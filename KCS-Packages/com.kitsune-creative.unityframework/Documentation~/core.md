# コア機能

## SingletonMonoBehaviour\<T\>

名前空間: `UnityFramework`

スレッドセーフなシングルトン基底クラス。シーン未配置時は自動生成され、ルート階層にあれば DontDestroyOnLoad が適用されます。

```csharp
using UnityFramework;

public class AudioMaster : SingletonMonoBehaviour<AudioMaster>
{
    public void Setup() { /* ... */ }

    protected override void Awake()
    {
        base.Awake();           // 必ず呼ぶ
        if (Instance != this) return;
        // 初期化処理
    }
}

// アクセス時に自動生成 (シーンに存在しなければ)
AudioMaster.Instance.Setup();

// 自動生成を避けたい場合
if (AudioMaster.HasInstance) AudioMaster.Instance.Setup();
```

### オーバーライド可能なライフサイクル

| メソッド | 役割 |
|---|---|
| `Awake()` | 重複インスタンス検出 + DontDestroyOnLoad |
| `OnApplicationQuit()` | 終了フラグセット (以降 `Instance` は null を返す) |
| `OnDestroy()` | インスタンス参照クリア |

`base.~()` を必ず呼んでください。

### プロパティ

- `Instance` — 取得時に未生成なら `FindAnyObjectByType` → 自動生成
- `HasInstance` — 生成済みなら true (副作用なし)

## SafeLogger

名前空間: `UnityFramework`

リリースビルドで `Log` / `LogWarning` が `[Conditional]` 属性で完全に除去されるラッパー。`LogError` / `LogException` は常時有効。

```csharp
using UnityFramework;

SafeLogger.Log("Info message");                    // Editor/DevBuild/ENABLE_LOG時のみ
SafeLogger.Log("With context", contextObject);
SafeLogger.LogFormat("Player {0}", playerId);
SafeLogger.LogWarning("Warning");

SafeLogger.LogError("Always logged");              // 常時
SafeLogger.LogException(ex);                       // 常時
```

### 有効化条件

以下のいずれかが定義されていれば `Log` / `LogWarning` も出力されます:

- `UNITY_EDITOR` (Editor 実行時は常に)
- `DEVELOPMENT_BUILD` (Development Build 時)
- `ENABLE_LOG` (任意の Define Symbol。リリースでも出したい場合)

## ServiceLocator

名前空間: `UnityFramework`

型キーの簡易 DI コンテナ。スレッドセーフ。

```csharp
using UnityFramework;

// 登録
ServiceLocator.Register<IPlayerData>(new PlayerData());
ServiceLocator.RegisterOrReplace<IPlayerData>(newData);  // 上書きOK

// 取得
var data = ServiceLocator.Resolve<IPlayerData>();        // 未登録なら例外
var maybe = ServiceLocator.TryResolve<IPlayerData>();    // 未登録ならnull
if (ServiceLocator.TryResolve<IPlayerData>(out var d)) Use(d);

// 確認/解除
bool exists = ServiceLocator.IsRegistered<IPlayerData>();
ServiceLocator.Unregister<IPlayerData>();
ServiceLocator.Clear();                                   // 全解除
```

`SingletonMonoBehaviour` との使い分け:

| 用途 | 選択 |
|---|---|
| シーンに紐づく実体が必要 (AudioSource 等) | `SingletonMonoBehaviour<T>` |
| インターフェイス越しに差し替えたい (テスト・モック) | `ServiceLocator` |

## ResourceController

名前空間: `UnityFramework.Resource`

Addressables ベースの非同期アセットローダー。同一アドレスへの多重ロードを内部キャッシュで合流し、参照カウントで管理します。

```csharp
using UnityFramework.Resource;

// 単発ロード (参照カウント +1)
var clip = await ResourceController.Instance.LoadAsync<AudioClip>("BGM/Title");

// 事前ロード (戻り値を捨てる用途。LoadAsyncと同じ挙動)
await ResourceController.Instance.PreloadAsync<AudioClip>("BGM/Title");

// 並列複数ロード
await ResourceController.Instance.PreloadAllAsync<AudioClip>(new[] {
    "SE/Click", "SE/Cancel", "SE/Decide"
});

// 解放 (参照カウント -1、0で実際にAddressablesが解放)
ResourceController.Instance.Release("BGM/Title");

// 同期取得 (キャッシュヒット時のみ、未ロード/ロード中はnull)
var cached = ResourceController.Instance.Get<AudioClip>("SE/Click");

// 確認
bool loaded = ResourceController.Instance.IsLoaded("SE/Click");

// 全解放 (シーン切替時など)
ResourceController.Instance.ReleaseAll();
```

### キャンセル

```csharp
var cts = new CancellationTokenSource();
try
{
    var clip = await ResourceController.Instance.LoadAsync<AudioClip>("BGM/Title", cts.Token);
}
catch (OperationCanceledException)
{
    // キャンセル時はキャッシュエントリも削除される
}
```
