# Layered Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** MainWindow の神クラスを View / Service / Infrastructure に分離し、インターフェース抽象化で CallService のテストを書けるようにする。

**Architecture:**
```
View層              … WPFウィンドウ。イベントをServiceに委譲し、状態を読んで表示する。
Service層           … ビジネスロジック。通話ステートマシン。インターフェースを依存とし、テスト可能。
Infrastructure層    … 外部サービスの実装。SignalR・gRPC・NAudio を直接扱う。
Repository層        … データアクセス。電話帳リスト・デバイス列挙。
Model層             … データクラス・列挙型。
```

**Tech Stack:** .NET 8 WPF, SignalR Client, gRPC, NAudio, xUnit + Moq（テスト用・新規追加）

---

## 現状の問題点

### 問題A: MainWindow が全ての責務を持っている（683行の神クラス）

`MainWindow.cs` に以下が全部混ぜている：

| 責務 | 該当 |
|---|---|
| 通話ステートマシン | `StartOutgoingCall`, `HandleIncomingCall`, `AnswerCall`, `EndCall`, `ToggleHold/Mute/Speaker` など |
| サーバー接続管理 | `ConnectToServer`, `SetupSignalREvents` |
| オーディオデバイス状態 | `_currentInputDevice`, `_normalOutputDevice` など |
| UI構築・表示 | `InitializeComponents`(120行), `Create*View`, 色定数 |
| 電話帳検索 | `PhoneBook.Entries.FirstOrDefault(...)` |

`PhoneStatus` enum も `MainWindow.cs` の先頭に混ぜてある。

### 問題B: 3つのManagerが全部テスト不可能

3つのManagerは全部、**外部実装クラスを内部で直接 `new` して持っている**。
そのため、テストで「サーバーに実際に接続せず」「スピーカーに音を出さず」に動作を検証できない。

| クラス | 直接 new で持っている実装 | なぜテスト不可 |
|---|---|---|
| `CommunicationManager` | `HubConnection`（SignalR実装） | サーバーに実際に接続しないと動かない |
| `GrpcVoiceManager` | `GrpcChannel`, `WaveInEvent`, `WaveOutEvent` | gRPCサーバー・マイク・スピーカーが必要 |
| `SoundManager` | `WaveOutEvent`, `AudioFileReader` | スピーカー・WAVファイルが必要 |

さらに、現在の `CallService`（予定）のコンストラクタも `new CommunicationManager()` で直接生成するため、テストでモックを渡せない。

### 解決策の選び方

> `HubConnection` や `GrpcChannel` 自体をインターフェースにするのは過度（外部ライブラリの型になる）。
> **各Managerの公開インターフェース自体を抽象化**し、CallServiceがインターフェースを受け取るようにする。
> テストでは Mock実装を渡し、本番では Real実装を渡す。

---

## リファクタ後のファイル構成

```
Tatehama_tetuden/
├── Models/
│   ├── PhoneBookEntry.cs          … 既存クラス移動
│   ├── DeviceInfo.cs              … 既存クラス移動
│   └── PhoneStatus.cs             … enum 移動
│
├── Contracts/                     … インターフェース定義
│   ├── ISignalingService.cs       … CommunicationManager の公開面
│   ├── IVoiceService.cs           … GrpcVoiceManager の公開面
│   └── ISoundService.cs           … SoundManager の公開面
│
├── Infrastructure/                … 実装（外部サービスを直接扱う）
│   ├── SignalingService.cs        … 現在の CommunicationManager のコード
│   ├── VoiceService.cs            … 現在の GrpcVoiceManager のコード
│   └── SoundService.cs            … 現在の SoundManager のコード
│
├── Repositories/
│   ├── PhoneBookRepository.cs     … 電話帳リスト
│   └── AudioDeviceRepository.cs   … デバイス列挙
│
├── Services/
│   └── CallService.cs             … 通話ステートマシン。インターフェースを依存とする
│
├── MainWindow.cs                  … View（薄い）
├── AudioSettingWindow.cs          … View
├── StationSelectionWindow.cs     … View
├── Program.cs                     … エントリポイント
│
└── UdpVoiceManager.cs            … 未使用。この計画では触れない
```

**削除するファイル:**
- `CommunicationManager.cs` → `Infrastructure/SignalingService.cs` へ
- `GrpcVoiceManager.cs` → `Infrastructure/VoiceService.cs` へ
- `SoundManager.cs` → `Infrastructure/SoundService.cs` へ
- `PhoneBook.cs` → `Repositories/PhoneBookRepository.cs` へ
- `DeviceInfo.cs` → `Models/DeviceInfo.cs` へ

テストプロジェクト:
```
Tatehama_tetuden.Tests/
├── Tatehama_tetuden.Tests.csproj
└── CallServiceTests.cs
```

---

## インターフェース設計（Contracts）

これが「テスト書けるかどうか」の鍵になる。各インターフェースは **実際に CallService が使う操作だけ**に絞る。

### ISignalingService — シグナリング（現在の CommunicationManager の公開面）

```csharp
// Contracts/ISignalingService.cs
namespace RailwayPhone;

public interface ISignalingService : IDisposable
{
    // --- 接続状態 ---
    bool IsConnected { get; }

    // --- 接続・ログイン ---
    Task<bool> ConnectAsync(string ipAddress, int port);
    void SendLogin(string myNumber);

    // --- 呼び出し制御 ---
    void SendCall(string targetNumber);
    void SendAnswer(string targetNumber, string callerId);
    void SendReject(string callerId);
    void SendHangup(string targetId);
    void SendBusy(string callerId);
    void SendHold(string targetId);
    void SendResume(string targetId);

    // --- イベント ---
    event Action<string>         LoginSuccess;           // (my_id)
    event Action<string, string> IncomingCallReceived;   // (fromNumber, callerId)
    event Action<string>         AnswerReceived;         // (responderId)
    event Action<string>         HangupReceived;         // (fromId)
    event Action<string>         CancelReceived;         // (fromId)
    event Action<string>         RejectReceived;         // (fromId)
    event Action                 BusyReceived;
    event Action                 HoldReceived;
    event Action                 ResumeReceived;
    event Action                 ConnectionLost;
    event Action                 Reconnecting;
    event Action                 Reconnected;
}
```

### IVoiceService — 音声送受信（現在の GrpcVoiceManager の公開面）

```csharp
// Contracts/IVoiceService.cs
namespace RailwayPhone;

public interface IVoiceService : IDisposable
{
    bool IsMuted { get; set; }

    void StartTransmission(string myId, string targetId, string serverIp, int serverPort, int inputDevId, int outputDevId);
    void StopTransmission();
    void ChangeOutputDevice(int outputDeviceId);
}
```

### ISoundService — 効果音再生（現在の SoundManager の公開面）

```csharp
// Contracts/ISoundService.cs
namespace RailwayPhone;

public interface ISoundService : IDisposable
{
    // ファイル名定数はここに置かない。
    // SoundName enum を別途定義し、実装側がファイルパスに変換する。
    void Play(string soundName, bool loop = false, int loopIntervalMs = 0);
    void Stop();
    void SetOutputDevice(string? deviceIdStr);
}
```

> **なぜ `SoundManager.FILE_YOBI1` のような文字列定数を Interface に残さないか:**
> インターフェースはコントラクト（約束）だけ表現すべき。WAVファイル名はインフラの実装詳細。
> ただし `CallService` 側から呼ぶときに `"yobi1.wav"` のような文字列リテラルを書くのも悪い。
> そのため **`SoundName` 定数クラス**を `Models/` に置き、CallService はそちらを参照する。
> 実装側（SoundService）がファイルパスに変換する。

```csharp
// Models/SoundName.cs
namespace RailwayPhone;

/// <summary>効果音識別名の定数。実装側がファイルパスに変換する。</summary>
public static class SoundName
{
    public const string Yobi1      = "yobi1";
    public const string Yobi2      = "yobi2";
    public const string Yobidashi  = "yobidashi";
    public const string Tori       = "tori";
    public const string Oki        = "oki";
    public const string Hold1      = "hold1";
    public const string Hold2      = "hold2";
    public const string Watyu      = "watyu";
}
```

---

## CallService の設計（インターフェース依存版）

```csharp
public class CallService : IDisposable
{
    private readonly ISignalingService _signaling;
    private readonly IVoiceService    _voice;
    private readonly ISoundService    _sound;
    private readonly PhoneBookRepository _phoneBookRepo;

    // コンストラクタインジェクション — テストでモックを渡せる
    public CallService(ISignalingService signaling, IVoiceService voice, ISoundService sound, PhoneBookRepository phoneBookRepo)
    {
        _signaling     = signaling;
        _voice         = voice;
        _sound         = sound;
        _phoneBookRepo = phoneBookRepo;
    }

    // ... (プロパティ・イベント・メソッドは以下のタスク6で実装)
}
```

本番のコンストラクタ呼び出し（MainWindow側）:
```csharp
var callService = new CallService(
    new SignalingService(),
    new VoiceService(),
    new SoundService(),
    new PhoneBookRepository()
);
```

テストのコンストラクタ呼び出し:
```csharp
var mockSignaling = new Mock<ISignalingService>();
var mockVoice     = new Mock<IVoiceService>();
var mockSound     = new Mock<ISoundService>();
var callService   = new CallService(mockSignaling.Object, mockVoice.Object, mockSound.Object, new PhoneBookRepository());
```

---

## テストプロジェクトの設定

```xml
<!-- Tatehama_tetuden.Tests/Tatehama_tetuden.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>   <!-- windowsでなくてよい。UIテストではない -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk"  Version="17.8.1" />
    <PackageReference Include="xunit"                   Version="2.6.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.6.1" />
    <PackageReference Include="Moq"                     Version="4.10.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Tatehama_tetuden/Tatehama_tetuden.csproj" />
  </ItemGroup>
</Project>
```

> **注意:** テストプロジェクトは `net8.0`（WPF なし）で十分。
> `CallService` は WPF に依存しないように設計されているため。

---

## タスク実施順序の考え方

```
Task 1  Model移動（PhoneStatus・PhoneBookEntry・DeviceInfo・SoundName）
Task 2  Contracts（インターフェース）を作成
Task 3  Infrastructure（実装）を作成（既存3コード → リネーム＋implements）
Task 4  Repository を作成
Task 5  CallService を作成（インターフェース依存）
Task 6  CallService に通話ロジックを実装
Task 7  テストプロジェクトを作成・CallService のテストを書く
Task 8  MainWindow を薄くする
Task 9  古いファイルを削除・最終確認
```

各タスクで検証は `dotnet build` で 0エラー確認。テスト書いたら `dotnet test` で確認。

---

### Task 1: Model を Models/ に移動

**Files:**
- Create: `Tatehama_tetuden/Models/PhoneStatus.cs`
- Create: `Tatehama_tetuden/Models/PhoneBookEntry.cs`
- Create: `Tatehama_tetuden/Models/DeviceInfo.cs`
- Create: `Tatehama_tetuden/Models/SoundName.cs`
- Modify: `Tatehama_tetuden/MainWindow.cs` (17行目の `enum PhoneStatus` を削除)
- Modify: `Tatehama_tetuden/PhoneBook.cs` (`PhoneBookEntry` クラス定義を削除し、`PhoneBook` クラスだけ残す)
- Delete: `Tatehama_tetuden/DeviceInfo.cs`

**Step 1: 4つの新ファイルを作成**

```csharp
// Models/PhoneStatus.cs
namespace RailwayPhone;
public enum PhoneStatus { Idle, Incoming, Outgoing, Talking, Holding }
```

```csharp
// Models/PhoneBookEntry.cs
namespace RailwayPhone;
public class PhoneBookEntry
{
    public string Name     { get; set; }
    public string Number   { get; set; }
    public string Category { get; set; }
}
```

```csharp
// Models/DeviceInfo.cs
namespace RailwayPhone;
public class DeviceInfo
{
    public string Name { get; set; }
    public string ID   { get; set; }
    public override string ToString() => Name;
}
```

```csharp
// Models/SoundName.cs
namespace RailwayPhone;
public static class SoundName
{
    public const string Yobi1     = "yobi1";
    public const string Yobi2     = "yobi2";
    public const string Yobidashi = "yobidashi";
    public const string Tori      = "tori";
    public const string Oki       = "oki";
    public const string Hold1     = "hold1";
    public const string Hold2     = "hold2";
    public const string Watyu     = "watyu";
}
```

**Step 2: MainWindow.cs の17行目の `enum PhoneStatus` 定義を削除**

削除する行:
```csharp
    public enum PhoneStatus { Idle, Incoming, Outgoing, Talking, Holding }
```

**Step 3: PhoneBook.cs から `PhoneBookEntry` クラス定義（4-9行目）を削除**

`PhoneBook` クラス自体は残す（次のタスクで削除）。

**Step 4: DeviceInfo.cs を削除**

```bash
git rm Tatehama_tetuden/DeviceInfo.cs
```

**Step 5: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).
(namespace が全て `RailwayPhone` なので他ファイルの参照変更は不要)

**Step 6: Commit**

```bash
git add Tatehama_tetuden/Models/ Tatehama_tetuden/MainWindow.cs Tatehama_tetuden/PhoneBook.cs
git rm Tatehama_tetuden/DeviceInfo.cs
git commit -m "refactor: Model クラス・enum を Models/ に移動し SoundName 定数を追加"
```

---

### Task 2: Contracts（インターフェース）を作成

**Files:**
- Create: `Tatehama_tetuden/Contracts/ISignalingService.cs`
- Create: `Tatehama_tetuden/Contracts/IVoiceService.cs`
- Create: `Tatehama_tetuden/Contracts/ISoundService.cs`

**何も変更しない。インターフェース定義だけ作る。**

**Step 1: 3つのインターフェースを作成**

```csharp
// Contracts/ISignalingService.cs
namespace RailwayPhone;

public interface ISignalingService : IDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(string ipAddress, int port);
    void SendLogin(string myNumber);
    void SendCall(string targetNumber);
    void SendAnswer(string targetNumber, string callerId);
    void SendReject(string callerId);
    void SendHangup(string targetId);
    void SendBusy(string callerId);
    void SendHold(string targetId);
    void SendResume(string targetId);

    event Action<string>         LoginSuccess;
    event Action<string, string> IncomingCallReceived;
    event Action<string>         AnswerReceived;
    event Action<string>         HangupReceived;
    event Action<string>         CancelReceived;
    event Action<string>         RejectReceived;
    event Action                 BusyReceived;
    event Action                 HoldReceived;
    event Action                 ResumeReceived;
    event Action                 ConnectionLost;
    event Action                 Reconnecting;
    event Action                 Reconnected;
}
```

```csharp
// Contracts/IVoiceService.cs
namespace RailwayPhone;

public interface IVoiceService : IDisposable
{
    bool IsMuted { get; set; }

    void StartTransmission(string myId, string targetId, string serverIp, int serverPort, int inputDevId, int outputDevId);
    void StopTransmission();
    void ChangeOutputDevice(int outputDeviceId);
}
```

```csharp
// Contracts/ISoundService.cs
namespace RailwayPhone;

public interface ISoundService : IDisposable
{
    void Play(string soundName, bool loop = false, int loopIntervalMs = 0);
    void Stop();
    void SetOutputDevice(string? deviceIdStr);
}
```

**Step 2: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).

**Step 3: Commit**

```bash
git add Tatehama_tetuden/Contracts/
git commit -m "refactor: ISignalingService・IVoiceService・ISoundService インターフェース追加"
```

---

### Task 3: Infrastructure（実装クラス）を作成

既存の3つのManagerコードを**新しいファイルにコピーし、インターフェースを実装する**。
古いファイルは Task 9 で削除。この段階では両方残す（コンパイルは成功したままになる）。

**Files:**
- Create: `Tatehama_tetuden/Infrastructure/SignalingService.cs`
- Create: `Tatehama_tetuden/Infrastructure/VoiceService.cs`
- Create: `Tatehama_tetuden/Infrastructure/SoundService.cs`

**Step 1: SignalingService を作成**

`CommunicationManager.cs` の全コードを元にして以下の変更だけ加える：
- クラス名: `CommunicationManager` → `SignalingService`
- クラス宣言に `: ISignalingService` を追加（`IDisposable` は Interface側で継承済みなので削除）
- `Connect` メソッド名を `ConnectAsync` に変更
- それ以外のコード（HubConnection の生成・イベント発火・Send*メソッド）は**そのまま残す**

```csharp
// Infrastructure/SignalingService.cs
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

namespace RailwayPhone;

public class SignalingService : ISignalingService
{
    private HubConnection? _hubConnection;
    private bool _isManuallyDisconnecting = false;

    // イベント宣言（インターフェースで約束した全イベント）
    public event Action<string>?         LoginSuccess;
    public event Action<string, string>? IncomingCallReceived;
    public event Action<string>?         AnswerReceived;
    public event Action<string>?         HangupReceived;
    public event Action<string>?         CancelReceived;
    public event Action<string>?         RejectReceived;
    public event Action?                 BusyReceived;
    public event Action?                 HoldReceived;
    public event Action?                 ResumeReceived;
    public event Action?                 ConnectionLost;
    public event Action?                 Reconnecting;
    public event Action?                 Reconnected;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    // 以下は CommunicationManager のコードをそのままコピー。
    // Connect → ConnectAsync にリネームのみ。
    public async Task<bool> ConnectAsync(string ipAddress, int port) { /* CommunicationManagerのConnect と同じ実装 */ }
    private async Task RetryConnectionLoop()                          { /* 同じ */ }
    private void RegisterHandlers()                                   { /* 同じ */ }

    public async void SendLogin(string myNumber)                      { /* 同じ */ }
    public async void SendCall(string targetNumber)                   { /* 同じ */ }
    public async void SendAnswer(string targetNumber, string callerId){ /* 同じ */ }
    public async void SendReject(string callerId)                     { /* 同じ */ }
    public async void SendHangup(string targetId)                     { /* 同じ */ }
    public async void SendBusy(string callerId)                       { /* 同じ */ }
    public async void SendHold(string targetId)                       { /* 同じ */ }
    public async void SendResume(string targetId)                     { /* 同じ */ }

    public void Dispose() { _isManuallyDisconnecting = true; _hubConnection?.DisposeAsync(); }
}
```

**Step 2: VoiceService を作成**

`GrpcVoiceManager.cs` の全コードを元にして：
- クラス名: `GrpcVoiceManager` → `VoiceService`
- クラス宣言に `: IVoiceService` を追加
- それ以外は**そのまま残す**

```csharp
// Infrastructure/VoiceService.cs
// (gRPC・NAudio の using は CommunicationManager と同じ)

namespace RailwayPhone;

public class VoiceService : IVoiceService
{
    // GrpcVoiceManager の全フィールド・メソッドをそのまま残す
    // クラス名だけ変える
}
```

**Step 3: SoundService を作成**

`SoundManager.cs` の全コードを元にして：
- クラス名: `SoundManager` → `SoundService`
- クラス宣言に `: ISoundService` を追加
- `Play` メソッドの引数 `fileName` は `soundName` に変更し、**ファイル名に変換**する:

```csharp
public void Play(string soundName, bool loop = false, int loopIntervalMs = 0)
{
    Stop();
    // soundName → ファイル名へ変換（拡張子を追加）
    string fileName = soundName + ".wav";
    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", fileName);
    if (!File.Exists(path)) return;
    // 以下は SoundManager の Play と同じ
    // ...
}
```

- ファイル名定数（`FILE_YOBI1` など）は**削除**（`SoundName` クラスに移動済み）
- `LoopStream`・`IntervalLoopStream` 内部クラスはそのまま残す

**Step 4: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).
(古いManagerは残っているので、既存の参照は壊れない)

**Step 5: Commit**

```bash
git add Tatehama_tetuden/Infrastructure/
git commit -m "refactor: Infrastructure層に SignalingService・VoiceService・SoundService を追加"
```

---

### Task 4: Repository を作成

**Files:**
- Create: `Tatehama_tetuden/Repositories/PhoneBookRepository.cs`
- Create: `Tatehama_tetuden/Repositories/AudioDeviceRepository.cs`

**Step 1: PhoneBookRepository を作成**

```csharp
// Repositories/PhoneBookRepository.cs
namespace RailwayPhone;

public class PhoneBookRepository
{
    private static readonly List<PhoneBookEntry> _entries = new()
    {
        new PhoneBookEntry { Name = "総合司令所 館浜司令",  Number = "101", Category = "司令" },
        new PhoneBookEntry { Name = "総合司令所 館浜司令2", Number = "102", Category = "司令" },
        new PhoneBookEntry { Name = "総合司令所 サーバー室",  Number = "103", Category = "司令" },
        new PhoneBookEntry { Name = "総合司令所 サーバー室2", Number = "104", Category = "司令" },
        new PhoneBookEntry { Name = "館浜駅 信号扱所",       Number = "201", Category = "信号" },
        new PhoneBookEntry { Name = "駒野駅 信号扱所",       Number = "202", Category = "信号" },
        new PhoneBookEntry { Name = "津崎駅 信号扱所",       Number = "203", Category = "信号" },
        new PhoneBookEntry { Name = "浜園駅 信号扱所",       Number = "204", Category = "信号" },
        new PhoneBookEntry { Name = "新野崎駅 信号扱所",     Number = "205", Category = "信号" },
        new PhoneBookEntry { Name = "江ノ原検車区 信号扱所", Number = "206", Category = "信号" },
        new PhoneBookEntry { Name = "大道寺駅 信号扱所",     Number = "207", Category = "信号" },
        new PhoneBookEntry { Name = "藤江駅 信号扱所",       Number = "208", Category = "信号" },
        new PhoneBookEntry { Name = "水越駅 信号扱所",       Number = "209", Category = "信号" },
        new PhoneBookEntry { Name = "日野森駅 信号扱所",     Number = "210", Category = "信号" },
        new PhoneBookEntry { Name = "赤山町駅 信号扱所",     Number = "211", Category = "信号" },
        new PhoneBookEntry { Name = "館浜駅 乗務員詰所",     Number = "301", Category = "詰所" },
        new PhoneBookEntry { Name = "新井川 駅務室",         Number = "302", Category = "詰所" },
        new PhoneBookEntry { Name = "新野崎 駅長室",         Number = "303", Category = "詰所" },
        new PhoneBookEntry { Name = "赤山町駅 乗務員詰所",   Number = "304", Category = "詰所" },
        new PhoneBookEntry { Name = "駒野列車区",            Number = "401", Category = "列車区" },
        new PhoneBookEntry { Name = "大道寺列車区",          Number = "402", Category = "列車区" },
    };

    public List<PhoneBookEntry> GetAll() => _entries;

    public PhoneBookEntry? FindByNumber(string number)
        => _entries.FirstOrDefault(e => e.Number == number);
}
```

**Step 2: AudioDeviceRepository を作成**

`AudioSettingWindow.cs` の `LoadDevices()` メソッド中のデバイス列挙コードを読んで抽出する。

```csharp
// Repositories/AudioDeviceRepository.cs
using NAudio.Wave;

namespace RailwayPhone;

public class AudioDeviceRepository
{
    public List<DeviceInfo> GetInputDevices()
    {
        var devices = new List<DeviceInfo>();
        // LoadDevices の WaveIn列挙コードをここに移動
        // (実装者: AudioSettingWindow.cs の LoadDevices を読んで正確にコピーする)
        return devices;
    }

    public List<DeviceInfo> GetOutputDevices()
    {
        var devices = new List<DeviceInfo>();
        // LoadDevices の WaveOut列挙コードをここに移動
        return devices;
    }
}
```

**Step 3: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).

**Step 4: Commit**

```bash
git add Tatehama_tetuden/Repositories/
git commit -m "refactor: PhoneBookRepository・AudioDeviceRepository を追加"
```

---

### Task 5: CallService を作成（骨格・インターフェース依存）

**Files:**
- Create: `Tatehama_tetuden/Services/CallService.cs`

**Step 1: CallService の骨格を作成**

```csharp
// Services/CallService.cs
namespace RailwayPhone;

public class CallService : IDisposable
{
    // --- 依存（インターフェース） ---
    private readonly ISignalingService   _signaling;
    private readonly IVoiceService       _voice;
    private readonly ISoundService       _sound;
    private readonly PhoneBookRepository _phoneBookRepo;

    // --- サーバー接続設定 ---
    private const string SERVER_IP        = "192.168.1.100"; // ← MainWindow.cs:21 の実際の値を確認してコピーする
    private const int    SERVER_PORT      = 8888;
    private const int    SERVER_GRPC_PORT = 8889;

    // --- オーディオデバイス ---
    private DeviceInfo? _currentInputDevice;
    private DeviceInfo? _normalOutputDevice;
    private DeviceInfo? _speakerOutputDevice;

    // --- 接続状態 ---
    private string? _myConnectionId;
    public  bool    IsOnline { get; private set; }

    // --- 通話状態 ---
    public  PhoneStatus    CurrentStatus          { get; private set; } = PhoneStatus.Idle;
    public  PhoneBookEntry? CurrentStation        { get; private set; }
    public  string?         ConnectedTargetName   { get; private set; }
    public  DateTime?       CallStartTime         { get; private set; }
    public  bool            IsMuted               { get; private set; }
    public  bool            IsSpeakerOn           { get; private set; }
    public  bool            IsHolding             { get; private set; }
    public  bool            IsMyHold              { get; private set; }
    public  string          HoldStatusText        => IsMyHold ? "保留中" : "相手が保留";

    private string? _targetConnectionId;
    private string? _connectedTargetNumber;
    private bool    _isRemoteHold;

    // --- イベント（View がサブスクリブする） ---
    public event Action<PhoneStatus>?      StatusChanged;
    public event Action<bool>?             OnlineStateChanged;
    public event Action<string, string>?   IncomingCallReceived;  // (name, number)
    public event Action?                   CallEnded;

    /// <summary>コンストラクタインジェクション。テストでモックを渡す。</summary>
    public CallService(ISignalingService signaling, IVoiceService voice, ISoundService sound, PhoneBookRepository phoneBookRepo)
    {
        _signaling     = signaling;
        _voice         = voice;
        _sound         = sound;
        _phoneBookRepo = phoneBookRepo;
    }

    // --- 初期化 ---
    public void Initialize(PhoneBookEntry station, DeviceInfo? inputDev, DeviceInfo? normalOut, DeviceInfo? speakerOut) { /* TODO */ }

    // --- 接続 ---
    public async Task ConnectAsync() { /* TODO */ }
    private void SetupSignalREvents() { /* TODO */ }

    // --- ステーション・オーディオ設定 ---
    public void ChangeStation(PhoneBookEntry newStation)                                           { /* TODO */ }
    public void UpdateAudioDevices(DeviceInfo? input, DeviceInfo? normalOut)                       { /* TODO */ }

    // --- 通話操作（公開） ---
    public void StartCall(string targetNumber)  { /* TODO */ }
    public void AnswerCall()                    { /* TODO */ }
    public void EndCall()                       { /* TODO */ }
    public void ToggleMute()                    { /* TODO */ }
    public void ToggleSpeaker()                 { /* TODO */ }
    public void ToggleHold()                    { /* TODO */ }

    // --- 内部ハンドラ ---
    private void HandleIncomingCall(string fromNumber, string callerId) { /* TODO */ }
    private void HandleAnswered(string responderId)                     { /* TODO */ }
    private async void HandleBusySignal()                               { /* TODO */ }
    private async void HandleRejected()                                 { /* TODO */ }
    private void HandleRemoteHold(bool isHold)                          { /* TODO */ }
    private void StartHoldState(bool self)                              { /* TODO */ }
    private void StopHoldState()                                        { /* TODO */ }
    private void StartVoiceTransmission(string targetId)                { /* TODO */ }

    public void Dispose()
    {
        _voice?.Dispose();
        _sound?.Dispose();
        _signaling?.Dispose();
    }
}
```

**Step 2: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).

**Step 3: Commit**

```bash
git add Tatehama_tetuden/Services/CallService.cs
git commit -m "refactor: CallService 骨格を追加（インターフェース依存）"
```

---

### Task 6: CallService に通話ロジックを実装

**Files:**
- Modify: `Tatehama_tetuden/Services/CallService.cs`

MainWindow の各メソッドを読んで、「UI操作」を削除・「イベント発火」に置換して移動する。
`_commManager` → `_signaling`, `_voiceManager` → `_voice`, `_soundManager` → `_sound` に名前も変える。
`SoundManager.FILE_YOBI1` のような定数参照は `SoundName.Yobi1` に変える。

**変換規則一覧:**

| MainWindow | CallService |
|---|---|
| `_commManager.SendCall(...)` | `_signaling.SendCall(...)` |
| `_voiceManager.StartTransmission(...)` | `_voice.StartTransmission(...)` |
| `_soundManager.Play(SoundManager.FILE_TORI)` | `_sound.Play(SoundName.Tori)` |
| `_soundManager.Stop()` | `_sound.Stop()` |
| UI操作（`_outgoingNameText.Text = ...` など） | 削除。プロパティ更新・イベント発火で置換 |
| `Dispatcher.Invoke(...)` | 削除（View側がイベント受信時に自分で Invoke する） |

**実装の Representative 例:**

```csharp
public void Initialize(PhoneBookEntry station, DeviceInfo? inputDev, DeviceInfo? normalOut, DeviceInfo? speakerOut)
{
    CurrentStation       = station;
    _currentInputDevice  = inputDev;
    _normalOutputDevice  = normalOut;
    _speakerOutputDevice = speakerOut;
    SetupSignalREvents();
    _sound.SetOutputDevice(normalOut?.ID);
}

public async Task ConnectAsync()
{
    bool success = await _signaling.ConnectAsync(SERVER_IP, SERVER_PORT);
    if (success) { _signaling.SendLogin(CurrentStation!.Number); IsOnline = true; }
    else         { IsOnline = false; }
    OnlineStateChanged?.Invoke(IsOnline);
}

private void SetupSignalREvents()
{
    _signaling.LoginSuccess            += (id)                  => _myConnectionId = id;
    _signaling.IncomingCallReceived    += (number, callerId)    => HandleIncomingCall(number, callerId);
    _signaling.AnswerReceived          += (responderId)         => HandleAnswered(responderId);
    _signaling.HangupReceived          += (fromId)              => { if (string.IsNullOrEmpty(fromId) || fromId == _targetConnectionId) EndCall(sendSignal: false); };
    _signaling.CancelReceived          += (fromId)              => { if (CurrentStatus == PhoneStatus.Incoming && fromId == _targetConnectionId) EndCall(sendSignal: false, playSound: false); };
    _signaling.RejectReceived          += (fromId)              => HandleRejected();
    _signaling.BusyReceived            += ()                    => HandleBusySignal();
    _signaling.HoldReceived            += ()                    => HandleRemoteHold(true);
    _signaling.ResumeReceived          += ()                    => HandleRemoteHold(false);
    _signaling.ConnectionLost          += ()                    => { IsOnline = false; OnlineStateChanged?.Invoke(false); };
    _signaling.Reconnected             += ()                    => { _signaling.SendLogin(CurrentStation!.Number); IsOnline = true; OnlineStateChanged?.Invoke(true); };
}

public async void StartCall(string targetNumber)
{
    if (string.IsNullOrEmpty(targetNumber)) return;
    _connectedTargetNumber = targetNumber;
    ConnectedTargetName    = _phoneBookRepo.FindByNumber(targetNumber)?.Name ?? "未登録";

    if (!_signaling.IsConnected)
    {
        ConnectedTargetName = "圏外です";
        _sound.Play(SoundName.Watyu);
        CurrentStatus = PhoneStatus.Outgoing;
        StatusChanged?.Invoke(CurrentStatus);
        await Task.Delay(3000);
        if (CurrentStatus == PhoneStatus.Outgoing) EndCall(false, false);
        return;
    }

    CurrentStatus = PhoneStatus.Outgoing;
    StatusChanged?.Invoke(CurrentStatus);

    _sound.Play(SoundName.Tori);
    await Task.Delay(800);
    if (CurrentStatus == PhoneStatus.Outgoing)
    {
        _signaling.SendCall(targetNumber);
        _sound.Play(SoundName.Yobidashi, loop: true, loopIntervalMs: 2000);
    }
}

private void HandleIncomingCall(string fromNumber, string callerId)
{
    if (CurrentStatus != PhoneStatus.Idle) { _signaling.SendBusy(callerId); return; }
    _connectedTargetNumber = fromNumber;
    _targetConnectionId    = callerId;
    ConnectedTargetName    = _phoneBookRepo.FindByNumber(fromNumber)?.Name ?? "不明";

    CurrentStatus = PhoneStatus.Incoming;
    StatusChanged?.Invoke(CurrentStatus);
    _sound.Play(SoundName.Yobi1, loop: true, loopIntervalMs: 1000);
    IncomingCallReceived?.Invoke(ConnectedTargetName!, fromNumber);
}

public void AnswerCall()
{
    _sound.Stop();
    _sound.Play(SoundName.Tori);
    _signaling.SendAnswer(_connectedTargetNumber!, _targetConnectionId!);
    StartVoiceTransmission(_targetConnectionId!);
    CurrentStatus  = PhoneStatus.Talking;
    CallStartTime  = DateTime.Now;
    IsMuted = IsSpeakerOn = IsMyHold = _isRemoteHold = IsHolding = false;
    StatusChanged?.Invoke(CurrentStatus);
}

private void HandleAnswered(string responderId)
{
    if (CurrentStatus != PhoneStatus.Outgoing) return;
    _sound.Stop();
    _targetConnectionId = responderId;
    StartVoiceTransmission(_targetConnectionId);
    CurrentStatus  = PhoneStatus.Talking;
    CallStartTime  = DateTime.Now;
    IsMuted = IsSpeakerOn = IsMyHold = _isRemoteHold = IsHolding = false;
    StatusChanged?.Invoke(CurrentStatus);
}

// EndCall は sendSignal・playSound の2つのflag版を public・private で分ける
public void EndCall() => EndCallInternal(sendSignal: true, playSound: true);

private void EndCallInternal(bool sendSignal, bool playSound)
{
    if (CurrentStatus == PhoneStatus.Incoming && sendSignal && !string.IsNullOrEmpty(_targetConnectionId))
        _signaling.SendReject(_targetConnectionId);
    else if (sendSignal && !string.IsNullOrEmpty(_targetConnectionId))
        _signaling.SendHangup(_targetConnectionId);

    _voice.StopTransmission();
    _connectedTargetNumber = null;
    _targetConnectionId    = null;
    IsHolding = IsMyHold = _isRemoteHold = IsMuted = IsSpeakerOn = false;
    CallStartTime       = null;
    ConnectedTargetName = null;

    if (playSound) { _sound.SetOutputDevice(_normalOutputDevice?.ID); _sound.Play(SoundName.Oki); }
    else           { _sound.Stop(); }

    CurrentStatus = PhoneStatus.Idle;
    StatusChanged?.Invoke(CurrentStatus);
    CallEnded?.Invoke();
}

// 残り: ToggleMute, ToggleSpeaker, ToggleHold, HandleBusySignal, HandleRejected,
//       HandleRemoteHold, StartHoldState, StopHoldState, StartVoiceTransmission
// 全て MainWindow の実装を読んで、同じパターンで変換する。
```

**Step 1: 上記の実装を CallService.cs に適用**

**Step 2: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).

**Step 3: Commit**

```bash
git add Tatehama_tetuden/Services/CallService.cs
git commit -m "refactor: CallService に通話ロジック全体を実装"
```

---

### Task 7: テストプロジェクトを作成・CallService のテストを書く

**Files:**
- Create: `Tatehama_tetuden.Tests/Tatehama_tetuden.Tests.csproj`
- Create: `Tatehama_tetuden.Tests/CallServiceTests.cs`

**Step 1: テストプロジェクトを作成**

```bash
cd C:\Users\kota_\source\repos\Tatehama_tetuden
dotnet new xunit -o Tatehama_tetuden.Tests --force
```

**Step 2: csproj を編集**

生成された `Tatehama_tetuden.Tests.csproj` を以下に書き換える：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk"     Version="17.8.1" />
    <PackageReference Include="xunit"                      Version="2.6.1" />
    <PackageReference Include="xunit.runner.visualstudio"  Version="2.6.1" />
    <PackageReference Include="Moq"                        Version="4.10.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Tatehama_tetuden/Tatehama_tetuden.csproj" />
  </ItemGroup>
</Project>
```

**Step 3: CallServiceTests を書く**

```csharp
// Tatehama_tetuden.Tests/CallServiceTests.cs
using Moq;
using RailwayPhone;
using Xunit;

namespace RailwayPhone.Tests;

public class CallServiceTests
{
    private readonly Mock<ISignalingService>   _mockSignaling;
    private readonly Mock<IVoiceService>       _mockVoice;
    private readonly Mock<ISoundService>       _mockSound;
    private readonly PhoneBookRepository       _phoneBookRepo;
    private readonly CallService               _sut; // System Under Test

    public CallServiceTests()
    {
        _mockSignaling  = new Mock<ISignalingService>();
        _mockVoice      = new Mock<IVoiceService>();
        _mockSound      = new Mock<ISoundService>();
        _phoneBookRepo  = new PhoneBookRepository();

        _sut = new CallService(_mockSignaling.Object, _mockVoice.Object, _mockSound.Object, _phoneBookRepo);

        var station = new PhoneBookEntry { Name = "館浜駅 信号扱所", Number = "201", Category = "信号" };
        _sut.Initialize(station, null, null, null);
    }

    // --- 初期状態 ---

    [Fact]
    public void 初期状態はIdle()
    {
        Assert.Equal(PhoneStatus.Idle, _sut.CurrentStatus);
    }

    // --- 発信 ---

    [Fact]
    public void StartCall_接続済みの場合_Outgoingに遷移し_SendCallが呼ばれる()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);
        StatusChangedを受け取る(out var statusEvents);

        _sut.StartCall("101");

        // Outgoing に遷移されたことを確認
        Assert.Contains(PhoneStatus.Outgoing, statusEvents);
        // SendCall が呼び出されたことを確認（Task.Delayの後なので少し待つ）
        Task.Delay(1500).Wait();
        _mockSignaling.Verify(s => s.SendCall("101"), Times.Once);
    }

    [Fact]
    public void StartCall_接続されていない場合_圏外メッセージとWatyuが再生される()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(false);

        _sut.StartCall("101");

        Assert.Equal("圏外です", _sut.ConnectedTargetName);
        _mockSound.Verify(s => s.Play(SoundName.Watyu, false, 0), Times.Once);
    }

    [Fact]
    public void StartCall_空の番号の場合_何もしない()
    {
        _sut.StartCall("");

        Assert.Equal(PhoneStatus.Idle, _sut.CurrentStatus);
        _mockSignaling.Verify(s => s.SendCall(It.IsAny<string>()), Times.Never);
    }

    // --- 着信 ---

    [Fact]
    public void 着信_Idleの場合_Incomingに遷移し_着信音が再生される()
    {
        StatusChangedを受け取る(out var statusEvents);

        // CommunicationManagerのイベントを模倣してハンドラを呼ぶ
        SimulateIncomingCall("101", "caller-abc");

        Assert.Contains(PhoneStatus.Incoming, statusEvents);
        _mockSound.Verify(s => s.Play(SoundName.Yobi1, true, 1000), Times.Once);
    }

    [Fact]
    public void 着信_通話中の場合_Busyが返される()
    {
        // まず通話中にする
        SetupTalkingState();

        SimulateIncomingCall("102", "caller-xyz");

        _mockSignaling.Verify(s => s.SendBusy("caller-xyz"), Times.Once);
        // ステータスは変わらない
        Assert.Equal(PhoneStatus.Talking, _sut.CurrentStatus);
    }

    // --- 受話 ---

    [Fact]
    public void AnswerCall_着信中_Talkingに遷移し_音声開始される()
    {
        SimulateIncomingCall("101", "caller-abc");
        StatusChangedを受け取る(out var statusEvents);

        _sut.AnswerCall();

        Assert.Contains(PhoneStatus.Talking, statusEvents);
        _mockVoice.Verify(v => v.StartTransmission(It.IsAny<string>(), "caller-abc", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    // --- 切断 ---

    [Fact]
    public void EndCall_通話中_Hangupが送られ_Idleに戻る()
    {
        SetupTalkingState();
        StatusChangedを受け取る(out var statusEvents);

        _sut.EndCall();

        Assert.Contains(PhoneStatus.Idle, statusEvents);
        _mockSignaling.Verify(s => s.SendHangup(It.IsAny<string>()), Times.Once);
        _mockVoice.Verify(v => v.StopTransmission(), Times.Once);
    }

    [Fact]
    public void EndCall_着信中_Rejectが送られる()
    {
        SimulateIncomingCall("101", "caller-abc");

        _sut.EndCall();

        _mockSignaling.Verify(s => s.SendReject("caller-abc"), Times.Once);
    }

    // --- ミュート・スピーカー・保留 ---

    [Fact]
    public void ToggleMute_トグル動作が正しい()
    {
        Assert.False(_sut.IsMuted);
        _sut.ToggleMute();
        Assert.True(_sut.IsMuted);
        _sut.ToggleMute();
        Assert.False(_sut.IsMuted);
    }

    [Fact]
    public void ToggleSpeaker_トグル動作が正しい()
    {
        Assert.False(_sut.IsSpeakerOn);
        _sut.ToggleSpeaker();
        Assert.True(_sut.IsSpeakerOn);
    }

    [Fact]
    public void ToggleHold_自己保留_Holdが送られる()
    {
        SetupTalkingState();

        _sut.ToggleHold();

        Assert.True(_sut.IsMyHold);
        Assert.True(_sut.IsHolding);
        _mockSignaling.Verify(s => s.SendHold(It.IsAny<string>()), Times.Once);
        _mockSound.Verify(s => s.Play(SoundName.Hold1, true, 0), Times.Once);
    }

    [Fact]
    public void ToggleHold_保留中_Resumeが送られ_通話に戻る()
    {
        SetupTalkingState();
        _sut.ToggleHold(); // 保留中にする

        _sut.ToggleHold(); // 再開

        Assert.False(_sut.IsMyHold);
        Assert.False(_sut.IsHolding);
        _mockSignaling.Verify(s => s.SendResume(It.IsAny<string>()), Times.Once);
    }

    // --- 電話帳検索 ---

    [Fact]
    public void StartCall_登録番号_名前が正しくセットされる()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);

        _sut.StartCall("101"); // 総合司令所 館浜司令

        Assert.Equal("総合司令所 館浜司令", _sut.ConnectedTargetName);
    }

    [Fact]
    public void StartCall_未登録番号_未登録メッセージが表示される()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);

        _sut.StartCall("999");

        Assert.Equal("未登録", _sut.ConnectedTargetName);
    }

    // ─── ヘルパー ───────────────────────────────────────

    /// <summary>ISignalingService の着信イベントを発火させる（サーバーからの着信を模倣）</summary>
    private void SimulateIncomingCall(string fromNumber, string callerId)
    {
        // Moq のイベント発火: Setup で Action を取り出して手動で呼ぶ
        _mockSignaling.Raise(s => s.IncomingCallReceived += null, fromNumber, callerId);
    }

    /// <summary>StatusChanged イベントを記録するリストを返す</summary>
    private void StatusChangedを受け取る(out List<PhoneStatus> events)
    {
        events = new List<PhoneStatus>();
        var captured = events; // クロージャのため
        _sut.StatusChanged += (status) => captured.Add(status);
    }

    /// <summary>通話中状態にセットする（ヘルパー）</summary>
    private void SetupTalkingState()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);
        SimulateIncomingCall("101", "caller-abc");
        _sut.AnswerCall();
    }
}
```

**Step 4: テスト実行**

```bash
dotnet test Tatehama_tetuden.Tests/Tatehama_tetuden.Tests.csproj
```
Expected: 全テスト PASSED.

**Step 5: Commit**

```bash
git add Tatehama_tetuden.Tests/
git commit -m "test: CallService のテストを追加"
```

---

### Task 8: MainWindow を薄くする（View層にする）

**Files:**
- Modify: `Tatehama_tetuden/MainWindow.cs`
- Modify: `Tatehama_tetuden/AudioSettingWindow.cs` (AudioDeviceRepository を使うように変更)
- Modify: `Tatehama_tetuden/StationSelectionWindow.cs` (PhoneBookRepository を使うように変更)

**削除するフィールド（全部 CallService へ移動済み）:**
```
SERVER_IP, SERVER_PORT, SERVER_GRPC_PORT
_commManager, _voiceManager, _soundManager
_currentInputDevice, _normalOutputDevice, _speakerOutputDevice
_currentInputVol, _currentOutputVol
_myConnectionId, _targetConnectionId, _connectedTargetNumber
_isHolding, _isMyHold, _isRemoteHold, _isMuted, _isSpeakerOn
_callStartTime
```

**削除するメソッド（全部 CallService へ移動済み）:**
```
SetupSignalREvents, ConnectToServer, SetOfflineState, SetOnlineState, ChangeStatus
StartOutgoingCall, HandleIncomingCall, AnswerCall, HandleAnswered
StartVoiceTransmission, HandleBusySignal, HandleRejected, EndCall
ToggleMute, ToggleSpeaker, ToggleHold
HandleRemoteHold, StartHoldState, StopHoldState
```

**新しいコンストラクタ（本番のインスタンス生成）:**
```csharp
public MainWindow(PhoneBookEntry station)
{
    if (station == null) station = new PhoneBookEntry { Name = "設定なし", Number = "000" };

    Title = $"館浜電鉄 鉄道電話 - [{station.Name}]";
    Width = 950; Height = 650;
    WindowStartupLocation = WindowStartupLocation.CenterScreen;
    Background = _bgColor;

    _phoneBookRepo = new PhoneBookRepository();

    // Infrastructure実装を生成してCallServiceに渡す
    _callService = new CallService(
        new SignalingService(),
        new VoiceService(),
        new SoundService(),
        _phoneBookRepo
    );
    _callService.Initialize(station, null, null, null);

    InitializeComponents();
    SubscribeToCallService();

    _ = _callService.ConnectAsync();

    Closing += (s, e) => _callService.Dispose();
}
```

**イベントサブスクリプション・再描画ロジック:**
```csharp
private void SubscribeToCallService()
{
    _callService.StatusChanged         += (status)        => Dispatcher.Invoke(() => OnStatusChanged(status));
    _callService.OnlineStateChanged    += (online)        => Dispatcher.Invoke(() => OnOnlineStateChanged(online));
    _callService.IncomingCallReceived  += (name, number)  => Dispatcher.Invoke(() => OnIncomingCallReceived(name, number));
    _callService.CallEnded             += ()              => Dispatcher.Invoke(() => OnCallEnded());
}

private void OnStatusChanged(PhoneStatus status)
{
    switch (status)
    {
        case PhoneStatus.Idle:
            SwitchView(_viewKeypad);
            _inputNumberBox.Text = "";
            _statusNameText.Text = "宛先未指定";
            _statusNameText.Foreground = Brushes.Gray;
            if (_callBtn != null) _callBtn.Background = _primaryColor;
            StopAnimation(_talkingPulse); StopAnimation(_outgoingPulse); StopAnimation(_incomingPulse);
            _callTimer?.Stop();
            break;
        case PhoneStatus.Outgoing:
            SwitchView(_viewOutgoing);
            _outgoingNumberText.Text    = _callService.ConnectedTargetName ?? "";
            _outgoingNameText.Text      = _callService.ConnectedTargetName ?? "";
            _outgoingNameText.Foreground = _callService.IsOnline ? Brushes.Black : Brushes.Red;
            StartAnimation(_outgoingPulse, 0.8);
            break;
        case PhoneStatus.Talking:
            GoToTalkingScreen();
            break;
        case PhoneStatus.Holding:
            _talkingStatusText.Text = _callService.HoldStatusText;
            StopAnimation(_talkingPulse);
            UpdateButtonVisuals();
            break;
    }
}

private void OnOnlineStateChanged(bool online)
{
    if (online)
    {
        Title = $"館浜電鉄 鉄道電話 - [{_callService.CurrentStation?.Name}]";
        Background = _bgColor;
        if (_selfStationDisplay != null)
        {
            _selfStationDisplay.Text       = $"自局: {_callService.CurrentStation?.Name} ({_callService.CurrentStation?.Number})";
            _selfStationDisplay.Foreground = _primaryColor;
            _selfStationDisplay.Background = new SolidColorBrush(Color.FromRgb(230, 240, 255));
        }
    }
    else
    {
        Title = $"[圏外] {_callService.CurrentStation?.Name}";
        Background = _offlineBgColor;
        if (_selfStationDisplay != null)
        {
            _selfStationDisplay.Text       = "圏外";
            _selfStationDisplay.Foreground = Brushes.Gray;
            _selfStationDisplay.Background = Brushes.Transparent;
        }
    }
}

private void OnIncomingCallReceived(string name, string number)
{
    SwitchView(_viewIncoming);
    _incomingNameText.Text   = name;
    _incomingNumberText.Text = number;
    StartAnimation(_incomingPulse, 0.5);
    new ToastContentBuilder().AddText("着信あり").AddText($"{name} ({number})").Show();
}

private void OnCallEnded() { /* Idle への切り替えは OnStatusChanged で済む */ }
```

**ボタンクリックハンドラの変更:**
```csharp
// 発信
_callBtn.Click += (s, e) => _callService.StartCall(_inputNumberBox.Text.Trim());
// 受話
acceptBtn.Click += (s, e) => _callService.AnswerCall();
// 切断
hangupBtn.Click += (s, e) => _callService.EndCall();
// ミュート・スピーカー・保留
_muteBtn.Click    += (s, e) => { _callService.ToggleMute();    UpdateButtonVisuals(); };
_speakerBtn.Click += (s, e) => { _callService.ToggleSpeaker(); UpdateButtonVisuals(); };
_holdBtn.Click    += (s, e) => { _callService.ToggleHold();    UpdateButtonVisuals(); };
```

**UpdateButtonVisuals は _callService のプロパティで読む:**
```csharp
private void UpdateButtonVisuals()
{
    void Set(Button b, bool active) { if (b == null) return; b.Background = active ? _btnActiveBg : _btnInactiveBg; }
    Set(_muteBtn, _callService.IsMuted);
    Set(_speakerBtn, _callService.IsSpeakerOn);
    if (_holdBtn != null)
    {
        var stack = _holdBtn.Content as StackPanel;
        var text  = stack?.Children[1] as TextBlock;
        if (text != null) text.Text = _callService.IsMyHold ? "再 開" : "保 留";
    }
    Set(_holdBtn, _callService.IsMyHold);
}
```

**GoToTalkingScreen は View側に残す（UIタイマーは View管理）:**
```csharp
private void GoToTalkingScreen()
{
    SwitchView(_viewTalking);
    StopAnimation(_outgoingPulse);
    _talkingStatusText.Text       = "通話中";
    _talkingStatusText.Foreground = _acceptColor;
    if (_talkingIconBg != null) _talkingIconBg.Fill = _acceptColor;
    UpdateButtonVisuals();
    StartAnimation(_talkingPulse, 1.2);
    _talkingNameText.Text = _callService.ConnectedTargetName ?? "";

    _callTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _callTimer.Tick += (s, e) =>
    {
        if (_callService.CallStartTime.HasValue)
            _talkingTimerText.Text = DateTime.Now.Subtract(_callService.CallStartTime.Value).ToString(@"mm\:ss");
    };
    _callTimer.Start();
}
```

**設定画面の更新:**
```csharp
private void OpenAudioSettings(object sender, RoutedEventArgs e)
{
    var win = new AudioSettingWindow(_currentInputDevice, _normalOutputDevice, _currentInputVol, _currentOutputVol);
    win.Owner = this;
    if (win.ShowDialog() == true)
    {
        _currentInputDevice  = win.SelectedInput;
        _normalOutputDevice  = win.SelectedOutput;
        _currentInputVol     = win.InputVolume;
        _currentOutputVol    = win.OutputVolume;
        _callService.UpdateAudioDevices(win.SelectedInput, win.SelectedOutput);
    }
}

private void OpenStationSettings(object sender, RoutedEventArgs e)
{
    var win = new StationSelectionWindow(_callService.CurrentStation!);
    win.Owner = this;
    if (win.ShowDialog() == true) _callService.ChangeStation(win.SelectedStation!);
}
```

**AudioSettingWindow の LoadDevices を AudioDeviceRepository で置換:**
```csharp
// AudioSettingWindow に追加
private readonly AudioDeviceRepository _audioDeviceRepo = new();

// LoadDevices 中のデバイス列挙を置換
var inputs  = _audioDeviceRepo.GetInputDevices();
var outputs = _audioDeviceRepo.GetOutputDevices();
```

**StationSelectionWindow の PhoneBook.Entries を PhoneBookRepository で置換:**
```csharp
private readonly PhoneBookRepository _phoneBookRepo = new();
// PhoneBook.Entries → _phoneBookRepo.GetAll()
```

**Step 1: 上記の変更を適用**

**Step 2: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s). **ここが最もエラーが出やすい。出たらコンパイルエラーを読んで修正する。**

**Step 3: テスト再実行**

```bash
dotnet test Tatehama_tetuden.Tests/Tatehama_tetuden.Tests.csproj
```
Expected: 全テスト PASSED.

**Step 4: Commit**

```bash
git add Tatehama_tetuden/MainWindow.cs Tatehama_tetuden/AudioSettingWindow.cs Tatehama_tetuden/StationSelectionWindow.cs
git commit -m "refactor: MainWindow を View層に薄くする（CallService・Repository委譲）"
```

---

### Task 9: 古いファイルを削除・最終確認

**Files:**
- Delete: `Tatehama_tetuden/CommunicationManager.cs`
- Delete: `Tatehama_tetuden/GrpcVoiceManager.cs`
- Delete: `Tatehama_tetuden/SoundManager.cs`
- Delete: `Tatehama_tetuden/PhoneBook.cs`

**Step 1: 古いファイルを削除**

```bash
git rm Tatehama_tetuden/CommunicationManager.cs
git rm Tatehama_tetuden/GrpcVoiceManager.cs
git rm Tatehama_tetuden/SoundManager.cs
git rm Tatehama_tetuden/PhoneBook.cs
```

**Step 2: コンパイル確認**

```bash
dotnet build Tatehama_tetuden/Tatehama_tetuden.csproj
```
Expected: Build succeeded. 0 Error(s).
ここで「古い名前への参照が残っている」エラーが出る場合、Task 8 で見落とした参照がある。エラーメッセージで特定して修正する。

**Step 3: テスト再実行**

```bash
dotnet test Tatehama_tetuden.Tests/Tatehama_tetuden.Tests.csproj
```
Expected: 全テスト PASSED.

**Step 4: 動作確認のポイント**

アプリを起動して以下を手動で確認：
1. 自局選択画面が表示される
2. メインウィンドウが表示され、オンライン/オフライン状態が正しく表示される
3. キーパッドで番号入力 → 電話帳リストが連動する
4. 発信ボタン → 発信画面に遷移する
5. 音声設定画面 → デバイス選択が正しく動く

**Step 5: Commit**

```bash
git add .
git commit -m "refactor: 古いManager・PhoneBookを削除。レイヤーリファクタ完了"
```

---

## まとめ: リファクタ後の層の対応表

| 層 | ディレクトリ・ファイル | 責務 |
|---|---|---|
| **Model** | `Models/PhoneStatus.cs`, `Models/PhoneBookEntry.cs`, `Models/DeviceInfo.cs`, `Models/SoundName.cs` | データ構造・列挙型・定数 |
| **Contracts** | `Contracts/ISignalingService.cs`, `Contracts/IVoiceService.cs`, `Contracts/ISoundService.cs` | インターフェース定義（テスト可能にする鍵） |
| **Repository** | `Repositories/PhoneBookRepository.cs`, `Repositories/AudioDeviceRepository.cs` | データアクセス（電話帳リスト・デバイス列挙） |
| **Service** | `Services/CallService.cs` | 通話ステートマシン。インターフェースを依存とし、テスト可能 |
| **Infrastructure** | `Infrastructure/SignalingService.cs`, `Infrastructure/VoiceService.cs`, `Infrastructure/SoundService.cs` | 外部サービスの実装（SignalR・gRPC・NAudio） |
| **View** | `MainWindow.cs`, `AudioSettingWindow.cs`, `StationSelectionWindow.cs`, `Program.cs` | UI表示・イベント配線のみ |
| **Test** | `Tatehama_tetuden.Tests/CallServiceTests.cs` | CallService の単体テスト |
