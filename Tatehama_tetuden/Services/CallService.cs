using System;
using System.Threading.Tasks;

namespace RailwayPhone
{
    public class CallService : IDisposable
    {
        // --- 依存（インターフェース） ---
        private readonly ISignalingService   _signaling;
        private readonly IVoiceService       _voice;
        private readonly ISoundService       _sound;
        private readonly PhoneBookRepository _phoneBookRepo;

        // --- サーバー接続設定 ---
        private const string SERVER_IP        = "127.0.0.1";
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
        public  PhoneStatus     CurrentStatus          { get; private set; } = PhoneStatus.Idle;
        public  PhoneBookEntry? CurrentStation         { get; private set; }
        public  string?         ConnectedTargetName    { get; private set; }
        public  DateTime?       CallStartTime          { get; private set; }
        public  bool            IsMuted                { get; private set; }
        public  bool            IsSpeakerOn            { get; private set; }
        public  bool            IsHolding              { get; private set; }
        public  bool            IsMyHold               { get; private set; }
        public  string          HoldStatusText         => IsMyHold ? "保留中" : "相手が保留";

        private string? _targetConnectionId;
        private string? _connectedTargetNumber;
        private bool    _isRemoteHold;

        // --- イベント（View がサブスクリブする） ---
        public event Action<PhoneStatus>?      StatusChanged;
        public event Action<bool>?             OnlineStateChanged;
        public event Action<string, string>?   IncomingCallReceived;  // (name, number)
        public event Action?                   CallEnded;

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
        public void ChangeStation(PhoneBookEntry newStation)                             { /* TODO */ }
        public void UpdateAudioDevices(DeviceInfo? input, DeviceInfo? normalOut)         { /* TODO */ }

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
}
