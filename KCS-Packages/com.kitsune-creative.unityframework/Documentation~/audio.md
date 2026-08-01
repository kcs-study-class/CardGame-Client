# サウンド

名前空間: `UnityFramework.Audio`

## アーキテクチャ

```
SoundController (SingletonMonoBehaviour)
   └── ISoundBackend (バックエンド差替可能)
         ├── UnitySoundBackend   (AudioSource + ResourceController)
         └── CriAdxSoundBackend  (CRI ADX2 / criware-unity)
```

バックエンドは Inspector の **Backend Type** ドロップダウンで切替えます (`Unity` / `CriAdx`)。

## id (識別子) の形式

| Backend | id 形式 | 例 |
|---|---|---|
| Unity | Addressables アドレス | `"BGM/Title.ogg"` |
| CRI ADX | `"CueSheetName/CueName"` | `"MainBgm/Title"` |

## BGM API

```csharp
using UnityFramework.Audio;

// 即時再生
await SoundController.Instance.PlayBGMAsync("BGM/Title");

// クロスフェード (fadeTime > 0)
await SoundController.Instance.PlayBGMAsync("BGM/Battle", fadeTime: 2f);

// フェードアウトして停止
await SoundController.Instance.FadeOutBGMAsync(fadeTime: 1f);

// その他
SoundController.Instance.StopBGM();
SoundController.Instance.PauseBGM();
SoundController.Instance.ResumeBGM();
bool playing = SoundController.Instance.IsBGMPlaying;
```

## Intro + Loop BGM

`intro` を 1 回再生したあと `loop` に切れ目なく接続する定番パターン。

```csharp
// 文字列
await SoundController.Instance.PlayBGMWithIntroAsync(
    "BGM/second_dealing_intro",
    "BGM/second_dealing_loop",
    fadeTime: 1f);

// enum (生成済み SoundIds.cs と組み合わせて)
await SoundController.Instance.PlayBGMWithIntroAsync(
    BgmId.SecondDealingIntro,
    BgmId.SecondDealingLoop,
    id => id.ToAddress(),
    fadeTime: 1f);
```

### バックエンド別の挙動

| Backend | 実装 |
|---|---|
| Unity | 専用 `_bgmIntro` AudioSource + 通常の loop ソースを `AudioSource.PlayScheduled` で dsp 時刻精度に連結。サンプル単位で継ぎ目なし |
| CRI ADX | 警告ログ後、`loopId` のみを通常再生にフォールバック (CRI では Cue 内の Loop Start/End マーカーで表現するのが推奨) |

### Unity 側の注意点

- intro と loop の **サンプルレート (Hz) を揃える**こと。違うとフレームワークが警告ログを出します。継ぎ目に違和感が出る可能性
- intro クリップは `AudioClip.samples / AudioClip.frequency` で長さを取得して loop の `PlayScheduled` タイミングを計算
- intro が終わると `_bgmIntro` AudioSource は自動的に停止 (loop はかぶせずに継ぎ目スタート)
- 別 BGM への切替 (PlayBGMAsync/PlayBGMWithIntroAsync 再呼び出し) で intro が途中でも自動停止

### CRI ADX で同じ効果を出すには

CRI Atom Craft で 1 つの Cue として作成し、波形に Loop Start / Loop End マーカーを打ちます。再生は通常通り:

```csharp
await SoundController.Instance.PlayBGMAsync("MainBgm/SecondDealing", fadeTime: 1f);
```

## SE API

```csharp
// プールから再生 (同時再生数を超えた場合は最古のSEが上書きされる)
SoundController.Instance.PlaySE("SE/Click");
SoundController.Instance.PlaySE("SE/Click", volumeScale: 0.8f, pitch: 1.2f);

SoundController.Instance.StopAllSE();
```

SE プールサイズは Inspector の **SE Pool Size** で設定 (既定 8)。

## Preload / Unload

SE の初回再生レイテンシを抑えたいときに使用。

```csharp
// シーン開始時にまとめて先読み
await SoundController.Instance.PreloadAsync(new[] {
    "SE/Click", "SE/Cancel", "SE/Decide"
});

// 単発
await SoundController.Instance.PreloadAsync("BGM/Boss");

// シーン終了時にまとめて解放
SoundController.Instance.Unload("SE/Click");
```

| Backend | Preload 挙動 | Unload 挙動 |
|---|---|---|
| Unity | `ResourceController` の refCount を +1 してキャッシュ | refCount を -1 (0で実解放) |
| CRI ADX | no-op (Cue Sheet 単位のロード前提) | no-op |

## 音量制御

```csharp
SoundController.Instance.MasterVolume = 0.8f;   // 0.0〜1.0
SoundController.Instance.BGMVolume = 0.7f;
SoundController.Instance.SEVolume = 1.0f;
SoundController.Instance.IsMuted = false;
```

実効音量は `Master × BGM` または `Master × SE` (`IsMuted=true` の場合は 0)。

## 設定の永続化

```csharp
// 音量設定を PlayerPrefs に保存
SoundController.Instance.SavePreferences();

// 起動時に復元
void Start()
{
    SoundController.Instance.LoadPreferences();
}
```

PlayerPrefs キー:
- `UnityFramework.SoundController.MasterVolume`
- `UnityFramework.SoundController.BGMVolume`
- `UnityFramework.SoundController.SEVolume`
- `UnityFramework.SoundController.IsMuted`

## Audio Mixer 連携

Inspector で `BGM Mixer Group` / `SE Mixer Group` に AudioMixerGroup を割り当てると、内部の AudioSource にルーティングされます。

## Enum 自動生成

`Tools > UnityFramework > Generate Sound Enums` で Addressables ラベル別に enum を生成。

### 準備

1. Addressables Groups で各サウンドアセットにラベルを付与:
   - BGM 用: `BGM` ラベル
   - SE 用: `SE` ラベル
   - Voice 用: `Voice` ラベル
2. メニューから実行
3. `Assets/Generated/UnityFramework/SoundIds.cs` が生成される

### 生成内容 (例)

```csharp
namespace UnityFramework.Audio.Generated
{
    public enum BgmId { Title, Battle, Boss }

    public static class BgmIdExtensions
    {
        public static string ToAddress(this BgmId id) => id switch
        {
            BgmId.Title  => "BGM/Title.ogg",
            BgmId.Battle => "BGM/Battle.ogg",
            BgmId.Boss   => "BGM/Boss.ogg",
            _ => null,
        };
    }
}
```

### Enum で呼び出し

```csharp
using UnityFramework.Audio.Generated;

await SoundController.Instance.PlayBGMAsync(BgmId.Title, id => id.ToAddress());
SoundController.Instance.PlaySE(SeId.Click, id => id.ToAddress());

await SoundController.Instance.PreloadAsync(
    new[] { SeId.Click, SeId.Cancel, SeId.Decide },
    id => id.ToAddress());

SoundController.Instance.Unload(SeId.Click, id => id.ToAddress());
```

### ラベルマッピングのカスタマイズ

`Editor/SoundEnumGenerator.cs` の `LabelToEnumName` ディクショナリを編集:

```csharp
private static readonly Dictionary<string, string> LabelToEnumName = new Dictionary<string, string>
{
    { "BGM",   "BgmId" },
    { "SE",    "SeId" },
    { "Voice", "VoiceId" },
    { "Ambient", "AmbientId" },   // ← 追加例
};
```

## CRI ADX 連携

### 有効化手順

1. CRIWARE Unity SDK (criware-unity) を導入
2. **Player Settings > Other Settings > Scripting Define Symbols** に `UNITY_FRAMEWORK_USE_CRI` を追加
3. SoundController の Inspector で **Backend Type = CriAdx** に変更

### 前提条件

- CRIWARE Library Initializer / Error Handler がシーンに配置されていること
- 使用する Cue Sheet (.acb / .awb) は事前に `CriAtom.AddCueSheet` で登録済みであること
- id は `"CueSheetName/CueName"` 形式

### Cue Sheet ロード例

```csharp
// Addressables から .acb をロードして CRI に登録 (利用側で実装)
CriAtom.AddCueSheet("MainBgm", acbPath, awbPath);

// 以降は通常の API
await SoundController.Instance.PlayBGMAsync("MainBgm/Title", fadeTime: 1f);
SoundController.Instance.PlaySE("CommonSe/Click");
```

### CRI 固有の注意

- `pitch` は CRI 仕様に合わせて自動で cent 単位に変換 (1200 cent = 1 octave)
- Cue 個別の Preload は no-op (Cue Sheet 単位で事前ロードされる前提)
