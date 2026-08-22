# シーン

名前空間: `UnityFramework.SceneManagement`

## アーキテクチャ

```
SceneController          (シーン遷移ロジック + 進捗イベント)
   └── TransitionController  (フェード/カスタム演出オーバーレイ)
         └── ITransition       (カスタム遷移インターフェイス)
```

## 基本ロード

```csharp
using UnityFramework.SceneManagement;
using UnityEngine.SceneManagement;

await SceneController.Instance.LoadSceneAsync("MainMenu");
await SceneController.Instance.LoadSceneAsync("HUD", LoadSceneMode.Additive);
await SceneController.Instance.UnloadSceneAsync("HUD");
SceneController.Instance.SetActiveScene("MainMenu");

// Build Index 指定
await SceneController.Instance.LoadSceneAsync(buildIndex: 2);
```

## 進捗イベント

```csharp
SceneController.Instance.SceneLoadStarted += sceneName => { /* ... */ };
SceneController.Instance.SceneLoadProgress += (sceneName, progress) => {
    loadingBar.value = progress;       // 0.0〜1.0
};
SceneController.Instance.SceneLoadCompleted += sceneName => { /* ... */ };
SceneController.Instance.SceneUnloadCompleted += sceneName => { /* ... */ };

bool isLoading = SceneController.Instance.IsLoading;
```

## 最低表示時間付きロード

ローディング画面のチラつき防止に使用。

```csharp
await SceneController.Instance.LoadSceneWithMinimumDurationAsync(
    "MainMenu", minimumDuration: 1.5f);
```

進捗 0〜0.9 は実ロード、0.9〜1.0 は最低表示時間の経過に応じて補間されます。

## フェード遷移

`TransitionController` が自動的に生成され、フェードオーバーレイを挟みます。

```csharp
await SceneController.Instance.LoadSceneWithFadeAsync(
    "MainMenu",
    fadeOutDuration: 0.3f,
    fadeInDuration: 0.3f);
```

## Transition シーン挟み込み

Loading シーン等を経由する遷移パターン。

```csharp
await SceneController.Instance.LoadSceneViaTransitionSceneAsync(
    targetSceneName: "Stage01",
    transitionSceneName: "Loading",
    minimumDuration: 1.5f,
    fadeDuration: 0.3f);
```

### 処理フロー

1. フェードアウト (現在のシーンを覆う)
2. Transition シーンへ Single 切替
3. フェードイン (Transition シーンを表示)
4. ターゲットを背後ロード (`allowSceneActivation = false`)
5. 進捗 90% + 最低表示時間まで待機
6. フェードアウト
7. ターゲットを活性化 (Single モードのため Transition シーンは自動置換)
8. フェードイン

### Transition シーン内のローディング UI

```csharp
public class LoadingUI : MonoBehaviour
{
    [SerializeField] private Slider _progressBar;
    [SerializeField] private TMP_Text _percentText;

    private void OnEnable()
    {
        SceneController.Instance.SceneLoadProgress += OnProgress;
    }

    private void OnDisable()
    {
        if (SceneController.HasInstance)
            SceneController.Instance.SceneLoadProgress -= OnProgress;
    }

    private void OnProgress(string sceneName, float progress)
    {
        _progressBar.value = progress;
        _percentText.text = $"{Mathf.RoundToInt(progress * 100)}%";
    }
}
```

## TransitionController 直接操作

```csharp
using UnityFramework.SceneManagement;

// フェード制御
await TransitionController.Instance.FadeOutAsync(0.5f);
await TransitionController.Instance.FadeInAsync(0.5f);
await TransitionController.Instance.FadeAsync(from: 0f, to: 1f, duration: 0.5f);

// 状態
bool transitioning = TransitionController.Instance.IsTransitioning;
TransitionController.Instance.CurrentAlpha = 0.5f;
TransitionController.Instance.FadeColor = Color.white;
```

フェード中は `CanvasGroup.blocksRaycasts` + `interactable` により背後の UI 入力が自動ブロックされます (`alpha > 0.01` の間)。

## カスタムトランジション

```csharp
public sealed class CurtainTransition : ITransition
{
    public async Awaitable PlayOutAsync(CancellationToken ct)
    {
        // 左右カーテンを閉じる演出
    }

    public async Awaitable PlayInAsync(CancellationToken ct)
    {
        // カーテンを開く演出
    }
}

// 単発設定
TransitionController.Instance.CustomTransition = new CurtainTransition();
```

`CustomTransition` が設定されていると、`SceneController.LoadSceneWithFadeAsync` / `LoadSceneViaTransitionSceneAsync` の演出もフェードからカスタムに切り替わります。

## Transition Registry (複数演出切替)

```csharp
public enum TransitionType { Curtain, Wipe, Iris }

var t = TransitionController.Instance;

// 起動時に一括登録
t.RegisterTransition(TransitionType.Curtain.ToString(), new CurtainTransition());
t.RegisterTransition(TransitionType.Wipe.ToString(),    new WipeTransition());
t.RegisterTransition(TransitionType.Iris.ToString(),    new IrisTransition());

// 切替 (以降の SceneController 遷移演出に自動反映)
t.SetActiveTransition(TransitionType.Curtain, type => type.ToString());
t.SetActiveTransition("Wipe");                                // 文字列キーでもOK

// フェード (既定) に戻す
t.SetActiveTransition(null);

// アクティブキー確認
string current = t.ActiveTransitionKey;

// 解除 (アクティブだった場合は自動的にフェードに戻る)
t.UnregisterTransition(TransitionType.Curtain.ToString());
```

## Enum 自動生成

`Tools > UnityFramework > Generate Scene Enum` で **Build Settings** の enabled シーンから enum を生成。

enum の値 (`= 0, = 1, ...`) は Build Index と一致するため、`(int)SceneId.X` でそのまま Build Index として使えます。

### 生成内容 (例)

```csharp
namespace UnityFramework.SceneManagement.Generated
{
    public enum SceneId
    {
        Title    = 0,
        Loading  = 1,
        MainMenu = 2,
        Stage01  = 3,
    }

    public static class SceneIdExtensions
    {
        public static string ToSceneName(this SceneId id) => /* ... */;
        public static int ToBuildIndex(this SceneId id) => (int)id;
    }
}
```

### Enum で呼び出し

```csharp
using UnityFramework.SceneManagement.Generated;

// 基本
await SceneController.Instance.LoadSceneAsync(
    SceneId.MainMenu, id => id.ToSceneName());

// 最低表示時間付き
await SceneController.Instance.LoadSceneWithMinimumDurationAsync(
    SceneId.Stage01, id => id.ToSceneName(), minimumDuration: 1f);

// フェード付き
await SceneController.Instance.LoadSceneWithFadeAsync(
    SceneId.Stage01, id => id.ToSceneName());

// Transition シーン挟み込み
await SceneController.Instance.LoadSceneViaTransitionSceneAsync(
    targetSceneId: SceneId.Stage01,
    transitionSceneId: SceneId.Loading,
    nameResolver: id => id.ToSceneName(),
    minimumDuration: 1.5f);

// Build Index 直接
await SceneController.Instance.LoadSceneAsync(SceneId.Title.ToBuildIndex());
await SceneController.Instance.LoadSceneAsync((int)SceneId.Title);

// アクティブシーン
SceneController.Instance.SetActiveScene(SceneId.MainMenu, id => id.ToSceneName());

// アンロード
await SceneController.Instance.UnloadSceneAsync(SceneId.HUD, id => id.ToSceneName());
```

### 再生成

Build Settings のシーン追加・削除・並び替え後はメニューから再実行してください。生成ファイルは上書きされます。

## SceneBase (シーンコントローラ基底)

各シーンのルートコンポーネントは `SceneBase` を継承する。`IScenePreparer` の定型をまとめている:

- `PrepareAsync` — SceneController がフェードインの前に呼ぶ。2回目以降は無視 (1回実行保証)
- `OnStartAsync` — 既定は「エディタで直接再生したとき」のフォールバック (1フレーム後に未準備なら `PrepareAsync`)
- `TryBeginTransition()` / `CancelTransition()` / `IsTransitioning` — ボタン連打などによる遷移の二重起動防止

```csharp
public class Home : SceneBase
{
    protected override async Awaitable OnPrepareAsync(CancellationToken cancellationToken)
    {
        await LoadAssetsAsync(cancellationToken);   // アセットロード / UI 構築 / BGM
    }

    private async void OnPlayClicked()
    {
        if (!TryBeginTransition())
        {
            return;                                   // 既に遷移中
        }
        await SceneController.Instance.LoadSceneWithFadeAsync(SceneId.Lobby, SceneIdExtensions.ToSceneName);
    }
}
```

Start 時に別処理を挟みたい場合は `OnStartAsync` を override し、最後に `base.OnStartAsync()` を呼ぶ
(例: 必要なデータが無ければ別シーンへ退避)。
