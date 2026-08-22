# コーディング規約 (命名規則)

対象: **CardGame-Client** (`KTC.*`) と **com.kitsune-creative.unityframework** (`UnityFramework.*`)。
Go サーバー (CardGame-Server) は末尾の Go 章のみ。迷ったら既存コードの多数派に合わせ、この文書を更新すること。

## C# 命名

| 対象 | 規則 | 例 |
|---|---|---|
| クラス / 構造体 / enum | PascalCase | `HandEngine`, `SeatState`, `Street` |
| インターフェース | `I` + PascalCase | `IGameSession`, `IBotPolicy` |
| メソッド | PascalCase。非同期は `Async` 接尾辞 | `PrepareAsync`, `SendAction` |
| プロパティ | PascalCase | `MySeatIndex`, `IsComplete` |
| bool (プロパティ/フィールド/変数) | `Is` / `Has` / `Can` + | `IsConnected`, `HasSave`, `CanRaise` |
| イベント / Observable | 「名詞+過去分詞」or 完了形 | `StateUpdated`, `SafeAreaChanged`, `Connected` |
| 定数 / static readonly | PascalCase | `FontAddress`, `SeatOptions`, `BigBlind` |
| private フィールド | `_camelCase` (**static も同じ**) | `_session`, `_instance`, `_services` |
| `[SerializeField]` フィールド | **camelCase (アンダースコア無し)** | `potText`, `seatTemplate`, `ignoreTop` |
| ローカル変数 / 引数 | camelCase | `raiseTo`, `viewerSeat` |

### 禁止 (他流儀の混入)
- `m_` / `s_` プレフィックス (Unity 内部ソースの流儀)
- `SCREAMING_SNAKE_CASE` 定数 (C マクロの流儀)

### SerializeField の注意
- アンダーバー無しにするのは **Inspector 表示とシーン配線に合わせるため** (このプロジェクトはシーンベイク方式で参照が大量にあるため、表示名との一致を優先)
- 既存の `[SerializeField]` を改名するときは **必ず `[FormerlySerializedAs("旧名")]`** を付け、シーン/プレハブの配線を守る

## 名前空間 / asmdef

- ゲーム: `KTC.<領域>` — `KTC.Poker.Domain` / `KTC.Poker.Protocol` / `KTC.Poker.Session` / `KTC.Scene` / `KTC.SaveData` / `KTC.Boot` / `KTC.UI`
- フレームワーク: `UnityFramework.<領域>` — `UnityFramework.UI` / `UnityFramework.Boot` / `UnityFramework.Network` など
- asmdef 名 = ルート名前空間と一致させる (`KTC.Poker.Domain.asmdef` 等)
- 「アプリの知識 (AppSecret・PlayerData・卓ルール等) を持つものはゲーム側、持たないものはフレームワーク側」

## テスト

- クラス名: `<対象>Tests` (`HandEngineTests`)
- **メソッド名は日本語で仕様を書く**: `無料でチェックできるなら絶対に降りない()` / `v1セーブはv2フィールドがデフォルト0で読める()`

## Unity アセット / シーン

- GameObject: PascalCase (`StartButton`, `SafeArea`, `SeatTemplate`, `BlindsChangeButton`)
- テンプレート複製の子構造は**名前がコントラクト** (例: 席パネルの `Inner/Name/Stack/Bet/State`)。改名はコード側と同時に
- Addressables アドレス: `<グループ>/<名前>` (`SE/Click`, `BGM/Menu`, `Modals/Blinds`, `Fonts/NotoSansJP`, `Cards/cardBack_red2.png`)

## Git

- ブランチ: `feature/snake_case` → PR (base `develop`) → マージコミット。develop 直コミット禁止
- コミットメッセージ: `add : 日本語サマリ.` + 空行 + 箇条書きの詳細

## Go (CardGame-Server)

- `gofmt` 準拠。パッケージ名は小文字1語 (`poker`, `table`, `ws`)
- JSON タグはクライアントの JsonUtility に合わせ **camelCase** (`json:"handNumber"`)
- カード列は `[]int` にする (`[]byte` は encoding/json で base64 文字列になるため)
- テスト名は日本語可 (`Testサイドポット_3人オールイン`)
