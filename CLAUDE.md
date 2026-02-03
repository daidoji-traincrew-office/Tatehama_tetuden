# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

「館浜電鉄 鉄道電話」— 鉄道社内用の VoIP 電話クライアント（WPF アプリ）。
サーバー（SignalR ハブ + gRPC ボイスリレー）に接続し、駅や司令所の間で音声通話を行う。
クライアントのみが本リポジトリに収まっている。サーバー側は別リポジトリ。

- **ネームスペース**: `RailwayPhone`
- **ターゲットフレームワーク**: .NET 8.0 Windows (`net8.0-windows10.0.19041.0`)
- **UI フレームワーク**: WPF（XAML なし・コードビハインドのみで UI を構築）
- **ソリューション形式**: `.slnx`（Visual Studio の新形式）

## ビルド・実行

```bash
# プロジェクトルート（.slnx と Tatehama_tetuden/ が並ぶディレクトリ）から実行
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
dotnet run --project Tatehama_tetuden/Tatehama_tetuden.csproj

# リリースビルド
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj -c Release
```

テストプロジェクトは現時点で存在しない。

## プロト定義の再生成

`Protos/voice.proto` を変更した後、`dotnet build` で自動に再生成される（`.csproj` に `<Protobuf>` アイテムが定義されている）。

## アーキテクチャ・データフロー

```
Program.cs
  ├── StationSelectionWindow（起動時に自局選択モーダルを表示）
  └── MainWindow（メインウィンドウ・全ての通話ロジックの中心）
        ├── CommunicationManager   … SignalR via HubConnection
        │     └── サーバー :8888 /phoneHub へ接続
        │           イベント: 着信/回答/切断/拒否/話中/保留/再開
        ├── GrpcVoiceManager       … gRPC 双方向ストリーミングで音声送受信
        │     └── サーバー :8889 gRPC VoiceRelay サービス
        │           音声: 8kHz 16bit mono → MuLaw 圧縮 → proto VoiceData
        ├── SoundManager           … 効果音・呼び出し音の再生（NAudio）
        └── AudioSettingWindow     … マイク/スピーカー選択・音量・ループバックテスト
```

### シグナリング（SignalR）と音声（gRPC）の分離

- **シグナリング**は `CommunicationManager` が担当。全て `"ReceiveMessage"` ハンドラで受け取り、JSON の `type` フィールドでディスパッチする。
  - メッセージタイプ: `LOGIN_SUCCESS`, `INCOMING`, `ANSWERED`, `HANGUP`, `CANCEL`, `REJECT`, `BUSY`, `HOLD_REQUEST`, `RESUME_REQUEST`
- **音声データ**は `GrpcVoiceManager` が担当。通話開始時に gRPC チャンネルを生成し、`JoinSession` の双方向ストリームで音声を流す。通話終了時にチャンネルも破棄する。

### 通話ステートマシン

`PhoneStatus` 列挙型で管理される。`MainWindow` の右パネルに 4 つの画面があり、ステートに応じて切り替える。

```
Idle ─→ Outgoing ─→ Talking ─→ Holding
  ↑                    ↑            │
  └────────────────────┘            └──→ Talking
  ↑
Incoming ─→ Talking
```

- `Idle`: キーパッド表示。番号入力・電話帳選択で宛先を指定。
- `Outgoing`: 呼び出し中。`SendCall` で通知。相手の回答/拒否/話中で次へ。
- `Incoming`: 着信中。`SendBusy`（話中中）または `SendReject`（拒否）または `AnswerCall` で次へ。
- `Talking`: 通話中。ミュート・スピーカー・保留の操作が可能。
- `Holding`: 保留中。自己保留か相手保留かを flags で区別。

### 音声デバイス管理

- `DeviceInfo` が デバイス名と ID を保持（NAudio の WaveIn/WaveOut デバイス番号として使用）。
- `AudioSettingWindow` で WASAPI を使ってデバイス列挙・ループバックテスト。
- `GrpcVoiceManager` はスピーカー切り替え（通話中のスピーカーオン/オフ）に対応。

### UdpVoiceManager（後方互換・未使用）

`UdpVoiceManager.cs` は gRPC 移行前の UDP P2P 音声の実装。現在は `MainWindow` から参照されず、削除候補の残り。

## 電話帳（PhoneBook）

`PhoneBook.cs` に静的リスト `Entries` で定義。番号規則：
- `1xx`: 司令・サーバー室
- `2xx`: 駅信号扱所
- `3xx`: 詰所・駅務室・駅長室
- `4xx`: 列車区

電話帳の変更には `PhoneBook.Entries` のリストを編集する。現時点で外部ファイルや DB からの読み込みはない。

## ポート番号

- **8888**: SignalR ハブ（シグナリング・呼び出し制御）
- **8889**: gRPC VoiceRelay（音声データ）

これらは `MainWindow.cs` の定数 `SERVER_IP` / `SERVER_PORT` / `SERVER_GRPC_PORT` で定義されている。

## 効果音

`Sounds/` フォルダに WAV ファイルを配置。`SoundManager` の定数で参照される。`.csproj` で出力先にコピーされる。

| 定数 | ファイル | 用途 |
|---|---|---|
| `FILE_YOBI1` | yobi1.wav | 一般着信音 |
| `FILE_YOBI2` | yobi2.wav | 司令着信音 |
| `FILE_YOBIDASHI` | yobidashi.wav | 呼び出し音（ループ） |
| `FILE_TORI` | tori.wav | 受話器を取る音 |
| `FILE_OKI` | oki.wav | 受話器を置く音 |
| `FILE_HOLD1` | hold1.wav | 保留音 |
| `FILE_HOLD2` | hold2.wav | 保留音2 |
| `FILE_WATYU` | watyu.wav | 話し中音 |

## コード規則・注意事項

- UI は全て **コード生成**（XAML なし）。`InitializeComponents()` や `Create*View()` メソッドで構築される。
- スレッド安全: SignalR イベントコールバックは `Dispatcher.Invoke` で UI スレッドに戻す。
- `CommunicationManager` は自動再接続ループを持っている。接続断時は 5 秒ごとに再試行。再接続成功時に再度 `SendLogin` が必要。
- `GrpcVoiceManager` の gRPC チャンネルは通話開始時に生成・通話終了時に破棄（長時間保持しない）。
- `.csproj.user` と `*.suo` は `.gitignore` で除外されている。IDE 固有ファイルはコミットしない。
