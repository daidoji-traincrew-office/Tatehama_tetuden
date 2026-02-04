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

        /// <summary>コンストラクタインジェクション。テストでモックを渡す。</summary>
        public CallService(ISignalingService signaling, IVoiceService voice, ISoundService sound, PhoneBookRepository phoneBookRepo)
        {
            _signaling     = signaling;
            _voice         = voice;
            _sound         = sound;
            _phoneBookRepo = phoneBookRepo;
        }

        // --- 初期化 ---

        public void Initialize(PhoneBookEntry station, DeviceInfo? inputDev, DeviceInfo? normalOut, DeviceInfo? speakerOut)
        {
            CurrentStation       = station;
            _currentInputDevice  = inputDev;
            _normalOutputDevice  = normalOut;
            _speakerOutputDevice = speakerOut;
            SetupSignalREvents();
            _sound.SetOutputDevice(normalOut?.ID);
        }

        // --- 接続 ---

        public async Task ConnectAsync()
        {
            bool success = await _signaling.ConnectAsync(SERVER_IP, SERVER_PORT);
            if (success)
            {
                _signaling.SendLogin(CurrentStation!.Number);
                IsOnline = true;
            }
            else
            {
                IsOnline = false;
            }
            OnlineStateChanged?.Invoke(IsOnline);
        }

        private void SetupSignalREvents()
        {
            _signaling.LoginSuccess         += (id)               => { _myConnectionId = id; };
            _signaling.IncomingCallReceived += (number, callerId) => HandleIncomingCall(number, callerId);
            _signaling.AnswerReceived       += (responderId)      => HandleAnswered(responderId);
            _signaling.HangupReceived       += (fromId)           =>
            {
                if (string.IsNullOrEmpty(fromId) || fromId == _targetConnectionId)
                    EndCallInternal(sendSignal: false, playSound: true);
            };
            _signaling.CancelReceived       += (fromId)           =>
            {
                if (CurrentStatus == PhoneStatus.Incoming && fromId == _targetConnectionId)
                    EndCallInternal(sendSignal: false, playSound: false);
            };
            _signaling.RejectReceived       += (fromId)           => HandleRejected();
            _signaling.BusyReceived         += ()                 => HandleBusySignal();
            _signaling.HoldReceived         += ()                 => HandleRemoteHold(true);
            _signaling.ResumeReceived       += ()                 => HandleRemoteHold(false);
            _signaling.ConnectionLost       += ()                 => { IsOnline = false; OnlineStateChanged?.Invoke(false); };
            _signaling.Reconnected          += ()                 =>
            {
                _signaling.SendLogin(CurrentStation!.Number);
                IsOnline = true;
                OnlineStateChanged?.Invoke(true);
            };
        }

        // --- ステーション・オーディオ設定 ---

        public void ChangeStation(PhoneBookEntry newStation)
        {
            CurrentStation = newStation;
            if (_signaling.IsConnected)
            {
                _signaling.SendLogin(newStation.Number);
            }
            OnlineStateChanged?.Invoke(IsOnline);
        }

        public void UpdateAudioDevices(DeviceInfo? input, DeviceInfo? normalOut)
        {
            _currentInputDevice = input;
            _normalOutputDevice = normalOut;
            _sound.SetOutputDevice(normalOut?.ID);
        }

        // --- 通話操作（公開） ---

        public async void StartCall(string targetNumber)
        {
            if (string.IsNullOrEmpty(targetNumber)) return;

            _connectedTargetNumber = targetNumber;
            ConnectedTargetName    = _phoneBookRepo.FindByNumber(targetNumber)?.Name ?? "未登録";

            if (!_signaling.IsConnected)
            {
                ConnectedTargetName = "圏外です";
                CurrentStatus = PhoneStatus.Outgoing;
                StatusChanged?.Invoke(CurrentStatus);
                _sound.Play(SoundName.Watyu);
                await Task.Delay(3000);
                if (CurrentStatus == PhoneStatus.Outgoing)
                    EndCallInternal(sendSignal: false, playSound: false);
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

        public void AnswerCall()
        {
            _sound.Stop();
            _sound.Play(SoundName.Tori);
            _signaling.SendAnswer(_connectedTargetNumber!, _targetConnectionId!);
            StartVoiceTransmission(_targetConnectionId!);

            CurrentStatus  = PhoneStatus.Talking;
            CallStartTime  = DateTime.Now;
            IsMuted        = false;
            IsSpeakerOn    = false;
            IsMyHold       = false;
            _isRemoteHold  = false;
            IsHolding      = false;
            StatusChanged?.Invoke(CurrentStatus);
        }

        public void EndCall() => EndCallInternal(sendSignal: true, playSound: true);

        public void ToggleMute()
        {
            IsMuted = !IsMuted;
            if (!IsHolding) _voice.IsMuted = IsMuted;
            StatusChanged?.Invoke(CurrentStatus);
        }

        public void ToggleSpeaker()
        {
            IsSpeakerOn = !IsSpeakerOn;
            int id = -1;
            if (IsSpeakerOn && _speakerOutputDevice != null)  int.TryParse(_speakerOutputDevice.ID, out id);
            else if (!IsSpeakerOn && _normalOutputDevice != null) int.TryParse(_normalOutputDevice.ID, out id);
            _voice.ChangeOutputDevice(id);
            StatusChanged?.Invoke(CurrentStatus);
        }

        public void ToggleHold()
        {
            if (_isRemoteHold) return;
            IsMyHold = !IsMyHold;
            if (IsMyHold)
            {
                _signaling.SendHold(_targetConnectionId!);
                StartHoldState(self: true);
            }
            else
            {
                _signaling.SendResume(_targetConnectionId!);
                StopHoldState();
            }
        }

        // --- 内部ハンドラ ---

        private void HandleIncomingCall(string fromNumber, string callerId)
        {
            if (CurrentStatus != PhoneStatus.Idle)
            {
                _signaling.SendBusy(callerId);
                return;
            }

            _connectedTargetNumber = fromNumber;
            _targetConnectionId    = callerId;
            ConnectedTargetName    = _phoneBookRepo.FindByNumber(fromNumber)?.Name ?? "不明";

            CurrentStatus = PhoneStatus.Incoming;
            StatusChanged?.Invoke(CurrentStatus);

            _sound.Play(SoundName.Yobi1, loop: true, loopIntervalMs: 1000);
            IncomingCallReceived?.Invoke(ConnectedTargetName!, fromNumber);
        }

        private void HandleAnswered(string responderId)
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;

            _sound.Stop();
            _targetConnectionId = responderId;
            StartVoiceTransmission(_targetConnectionId);

            CurrentStatus  = PhoneStatus.Talking;
            CallStartTime  = DateTime.Now;
            IsMuted        = false;
            IsSpeakerOn    = false;
            IsMyHold       = false;
            _isRemoteHold  = false;
            IsHolding      = false;
            StatusChanged?.Invoke(CurrentStatus);
        }

        private async void HandleBusySignal()
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;
            ConnectedTargetName = "話中";
            _sound.Play(SoundName.Watyu);
            StatusChanged?.Invoke(CurrentStatus);
            await Task.Delay(5000);
            if (CurrentStatus == PhoneStatus.Outgoing)
                EndCallInternal(sendSignal: false, playSound: false);
        }

        private async void HandleRejected()
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;
            ConnectedTargetName = "事情によりお繋ぎできません";
            _sound.Stop();
            _sound.SetOutputDevice(_normalOutputDevice?.ID);
            _sound.Play(SoundName.Watyu);
            StatusChanged?.Invoke(CurrentStatus);
            await Task.Delay(5000);
            if (CurrentStatus == PhoneStatus.Outgoing)
                EndCallInternal(sendSignal: false, playSound: false);
        }

        private void HandleRemoteHold(bool isHold)
        {
            _isRemoteHold = isHold;
            if (isHold) StartHoldState(self: false);
            else        StopHoldState();
        }

        private void StartHoldState(bool self)
        {
            CurrentStatus = PhoneStatus.Holding;
            IsHolding     = true;
            _voice.IsMuted = true;
            _sound.Play(SoundName.Hold1, loop: true);
            StatusChanged?.Invoke(CurrentStatus);
        }

        private void StopHoldState()
        {
            CurrentStatus  = PhoneStatus.Talking;
            IsHolding      = false;
            _voice.IsMuted = IsMuted;
            _sound.Stop();
            StatusChanged?.Invoke(CurrentStatus);
        }

        private void StartVoiceTransmission(string targetId)
        {
            int inDev = -1, outDevId = -1;
            if (_currentInputDevice != null)  int.TryParse(_currentInputDevice.ID,  out inDev);
            if (_normalOutputDevice != null)  int.TryParse(_normalOutputDevice.ID,  out outDevId);
            _voice.StartTransmission(_myConnectionId ?? "", targetId, SERVER_IP, SERVER_GRPC_PORT, inDev, outDevId);
        }

        private void EndCallInternal(bool sendSignal, bool playSound)
        {
            if (sendSignal && !string.IsNullOrEmpty(_targetConnectionId))
            {
                if (CurrentStatus == PhoneStatus.Incoming)
                    _signaling.SendReject(_targetConnectionId);
                else
                    _signaling.SendHangup(_targetConnectionId);
            }

            _voice.StopTransmission();

            _connectedTargetNumber = null;
            _targetConnectionId    = null;
            IsHolding              = false;
            IsMyHold               = false;
            _isRemoteHold          = false;
            IsMuted                = false;
            IsSpeakerOn            = false;
            CallStartTime          = null;
            ConnectedTargetName    = null;

            if (playSound)
            {
                _sound.SetOutputDevice(_normalOutputDevice?.ID);
                _sound.Play(SoundName.Oki);
            }
            else
            {
                _sound.Stop();
            }

            CurrentStatus = PhoneStatus.Idle;
            StatusChanged?.Invoke(CurrentStatus);
            CallEnded?.Invoke();
        }

        public void Dispose()
        {
            _voice?.Dispose();
            _sound?.Dispose();
            _signaling?.Dispose();
        }
    }
}
