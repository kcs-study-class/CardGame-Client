# Unity Framework

Unity 6 (6000.0+) 向けの基礎フレームワーク。null 安全な拡張メソッド、シングルトン、サウンド/シーン/通信/リソース管理を統一的に提供します。

## 特徴

- **拡張メソッド** — Unity Object の "fake null" 対応 / GameObject・Transform・Vector ヘルパー / コレクション操作
- **SingletonMonoBehaviour\<T\>** — スレッドセーフなシングルトン基底クラス
- **SafeLogger** — リリースビルドで `Log`/`LogWarning` が自動的に除去される `[Conditional]` ロガー
- **ServiceLocator** — 型キーの簡易 DI
- **SoundController** — BGM/SE 管理。Unity AudioSource / CRI ADX バックエンド切替対応。Addressables 経由のロード、Preload/Unload、enum 呼び出し
- **SceneController** — 非同期シーン遷移、進捗イベント、ローディング画面サポート、Build Index 指定
- **TransitionController** — フェード/カスタム遷移演出。Transition シーン挟み込み対応、複数演出のレジストリ管理
- **NetworkControllerBase** — HTTP REST API クライアントの抽象基底
- **ResourceController** — Addressables ベースの参照カウント付きローダー
- **Editor ツール** — Addressables ラベルから Sound enum / Build Settings から Scene enum を自動生成

## 動作環境

- Unity 6 (6000.0+)
- 依存パッケージ: `com.unity.addressables` 2.2.2+
- 任意: CRIWARE Unity SDK (CRI ADX バックエンドを使う場合)

## インストール

### Packages/manifest.json で参照 (推奨)

```json
{
  "dependencies": {
    "com.kitsune-creative.unityframework": "file:../Packages/com.kitsune-creative.unityframework"
  }
}
```

または Unity Editor の **Window > Package Manager > + > Add package from disk** で `package.json` を選択。

### Assets 配置

`Runtime/` と `Editor/` フォルダを `Assets/Scripts/Framework/` 等にコピーしても動作します (asmdef ごとコピー)。

## クイックスタート

### Null 安全な参照

```csharp
using UnityFramework.Extensions;

if (target.IsNotNull()) target.DoSomething();

// 破棄済みUnityObjectをC#的なnullに変換 (null合体演算子と組み合わせる)
var go = mayBeDestroyed.OrNull() ?? fallback;
```

### シングルトン

```csharp
using UnityFramework;

public class GameController : SingletonMonoBehaviour<GameController>
{
    public void StartGame() { /* ... */ }
}

GameController.Instance.StartGame();
```

### サウンド再生 (enum)

```csharp
using UnityFramework.Audio;
using UnityFramework.Audio.Generated;  // 自動生成

await SoundController.Instance.PlayBGMAsync(BgmId.Title, id => id.ToAddress(), fadeTime: 1.5f);
SoundController.Instance.PlaySE(SeId.Click, id => id.ToAddress());
```

### シーン遷移 (enum + フェード)

```csharp
using UnityFramework.SceneManagement;
using UnityFramework.SceneManagement.Generated;

await SceneController.Instance.LoadSceneWithFadeAsync(
    SceneId.MainMenu, id => id.ToSceneName());

// Loading シーン挟み込み
await SceneController.Instance.LoadSceneViaTransitionSceneAsync(
    targetSceneId: SceneId.Stage01,
    transitionSceneId: SceneId.Loading,
    nameResolver: id => id.ToSceneName(),
    minimumDuration: 1.5f);
```

### HTTP API

```csharp
using UnityFramework.Network;

public sealed class MyApiClient : NetworkControllerBase
{
    protected override string BaseUrl => "https://api.example.com";
}

var client = new MyApiClient();
var res = await client.GetAsync<UserData>("/user/me");
if (res.IsSuccess) Use(res.Data);
```

## ドキュメント

詳細は `Documentation~/` 配下を参照:

- [拡張メソッド](Documentation~/extensions.md) — null チェック、GameObject、Transform、Vector、コレクション、文字列
- [コア機能](Documentation~/core.md) — SingletonMonoBehaviour、SafeLogger、ServiceLocator、ResourceController
- [サウンド](Documentation~/audio.md) — SoundController、Unity/CRI バックエンド、Preload、Enum 生成
- [シーン](Documentation~/scene.md) — SceneController、TransitionController、Transition シーン、Enum 生成
- [ネットワーク](Documentation~/network.md) — NetworkControllerBase、レスポンスハンドリング、認証

## フォルダ構成

```
Packages/com.kitsune-creative.unityframework/
├── package.json
├── README.md
├── Runtime/
│   ├── UnityFramework.Runtime.asmdef
│   ├── Audio/
│   │   ├── SoundController.cs
│   │   ├── ISoundBackend.cs
│   │   ├── SoundBackendType.cs
│   │   ├── UnitySoundBackend.cs
│   │   └── CriAdxSoundBackend.cs       (#if UNITY_FRAMEWORK_USE_CRI)
│   ├── Core/
│   │   ├── SingletonMonoBehaviour.cs
│   │   ├── SafeLogger.cs
│   │   └── ServiceLocator.cs
│   ├── Extensions/
│   │   ├── NullCheckExtensions.cs
│   │   ├── GameObjectExtensions.cs
│   │   ├── ComponentExtensions.cs
│   │   ├── TransformExtensions.cs
│   │   ├── CollectionExtensions.cs
│   │   ├── StringExtensions.cs
│   │   └── VectorExtensions.cs
│   ├── Network/
│   │   ├── NetworkControllerBase.cs
│   │   └── NetworkResponse.cs
│   ├── Resource/
│   │   └── ResourceController.cs
│   └── SceneManagement/
│       ├── SceneController.cs
│       ├── TransitionController.cs
│       └── ITransition.cs
├── Editor/
│   ├── UnityFramework.Editor.asmdef
│   ├── SoundEnumGenerator.cs
│   └── SceneEnumGenerator.cs
└── Documentation~/
    ├── extensions.md
    ├── core.md
    ├── audio.md
    ├── scene.md
    └── network.md
```

## 命名規約

- シングルトン基底: `SingletonMonoBehaviour<T>` (`Mono~` プレフィックス禁止)
- ドメイン管理クラス: `~Controller` (`SoundController`, `SceneController` 等)
- 抽象基底クラス: `~ControllerBase` (`NetworkControllerBase`)
- ユーティリティクラスは命名規約の対象外 (`SafeLogger`, `ServiceLocator`)
