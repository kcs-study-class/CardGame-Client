# CardGame-Client

テキサスホールデムの Unity クライアント (教材プロジェクト)。
まず**ローカル完結**で動くゲームを作り、その後**サーバー接続部分を生徒が実装する**二段構成。
サーバー側は [kcs-study-class/CardGame-Server](https://github.com/kcs-study-class/CardGame-Server) (Go + Docker)。

## 必要環境

- Unity **6.3 LTS (6000.3.8f1)** / URP
- UnityFramework パッケージ (`com.kitsune-creative.unityframework`) は**リポジトリ内に同梱**
  (`KCS-Packages/` を `Packages/manifest.json` から相対参照)。クローンするだけで開ける

## アーキテクチャ

サーバーオーソリタティブ前提の3層構成。**UI はゲーム状態を直接触らず、
アクション送信とスナップショット受信だけを行う**。

```
[UI (各シーン)]
      ↕ アクション送信 / スナップショット購読
[IGameSession]  ← シーム (ここが生徒課題の境界)
      ├─ LocalGameSession   … ローカル完結。エンジン + Bot 内蔵 (リファレンス実装)
      └─ RemoteGameSession  … 生徒が実装。WebSocket で Go サーバーへ
```

| アセンブリ | 場所 | 内容 |
|---|---|---|
| `KTC.Poker.Domain` | `Assets/Scripts/Poker/Domain/` | 純C#のルールエンジン (カード・役判定・ベッティング・サイドポット)。UnityEngine 非依存 |
| `KTC.Poker.Protocol` | `Assets/Scripts/Poker/Protocol/` | 通信メッセージDTO。**Goサーバーのプロトコル仕様書を兼ねる** |
| `KTC.Poker.Session` | `Assets/Scripts/Poker/Session/` | `IGameSession` シームと `LocalGameSession` / `IBotPolicy` |
| (Assembly-CSharp) | `Assets/Scripts/Scenes/`, `Ingame/`, `Network/` | シーンコントローラ・InGame UI・通信層 |

- カードの通信表現は **1 byte** (`(suit << 4) | rank`、A=14、0=裏面)
- プロトコル詳細: [CardGame-Server/Docs/API.md](https://github.com/kcs-study-class/CardGame-Server/blob/main/Docs/API.md)

## シーン構成 / 起動フロー

```
Boot → Logo(スキップ中) → Title → (TransitionLoading) → Home → InGame → Result
```

- `Title`: 任意ボタンで開始。裏でブート処理 (ログイン等) を行う予定
- `TransitionLoading`: シーン遷移時のローディング演出 (`SceneLoadProgress` 購読)
- `InGame`: 現在は **1人デバッグ卓** (`Assets/Scripts/Ingame/DebugTable.cs`) — 全席を自分で操作してルールエンジンを検証する
- エディタのツールバー「Boot Start」ボタンで Boot シーンから起動できる

## テスト

Test Runner (EditMode) でアセンブリ `KTC.Poker.Tests.EditMode` を実行。
役判定・ベッティング (ミニマムレイズ/ショートオールイン/サイドポット)・
セッション層 (リダクション/進行)・プロトコルのJSON往復を網羅 (57件)。

## 開発ロードマップ

実装タスクは [Issues](https://github.com/kcs-study-class/CardGame-Client/issues) で管理。
推奨順: セーブデータ層 → 日本語フォント+初回モーダル → デバッグメニュー → ホーム画面 → ブートパイプライン。

## 生徒課題 (予定)

- **クライアント班**: `RemoteGameSession` (`IGameSession` 実装) — WebSocket でサーバーと接続。UIコード無変更で Local ⇔ Remote を差し替える
- **サーバー班**: Go サーバー — [API.md](https://github.com/kcs-study-class/CardGame-Server/blob/main/Docs/API.md) / [DB.md](https://github.com/kcs-study-class/CardGame-Server/blob/main/Docs/DB.md) 準拠
- **拡張課題**: `IBotPolicy` を実装して強い Bot を作る
