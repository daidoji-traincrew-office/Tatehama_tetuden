using Microsoft.Extensions.Logging;
using Tatehama_tetuden.Models;
using Tatehama_tetuden.Repositories;

namespace Tatehama_tetuden.Services
{
    public class CallService : IDisposable, IAsyncDisposable
    {
        // --- 依存（インターフェース） ---
        private readonly ILogger<CallService> _logger;
        private readonly ISignalingRepository   _signaling;
        private readonly IVoiceRepository       _voice;
        private readonly ISoundRepository       _sound;
        private readonly IPhoneBookRepository _phoneBookRepo;

        // --- オーディオデバイス ---
        private DeviceInfo? _currentInputDevice;
        private DeviceInfo? _normalOutputDevice;
        private DeviceInfo? _speakerOutputDevice;

        // --- 接続状態 ---
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

        private string? _connectedTargetNumber;
        private bool    _isRemoteHold;

        // --- イベント（View がサブスクリブする） ---
        public event Action<PhoneStatus>?      StatusChanged;
        public event Action<bool>?             OnlineStateChanged;
        public event Action<string, string>?   IncomingCallReceived;  // (name, number)
        public event Action?                   CallEnded;

        // --- スレッド安全性 ---
        private readonly SemaphoreSlim _stateLock = new(1, 1);

        /// <summary>コンストラクタインジェクション。テストでモックを渡す。</summary>
        public CallService(ILogger<CallService> logger, ISignalingRepository signaling, IVoiceRepository voice, ISoundRepository sound, IPhoneBookRepository phoneBookRepo)
        {
            _logger        = logger;
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
            SetupSignalingEvents();
            _sound.SetOutputDevice(normalOut?.ID);
        }

        // --- 接続 ---

        public async Task ConnectAsync()
        {
            bool success = await _signaling.ConnectAsync();
            if (success)
            {
                await _signaling.SendLogin(CurrentStation!.Number);
                IsOnline = true;
            }
            else
            {
                IsOnline = false;
            }
            OnlineStateChanged?.Invoke(IsOnline);
        }

        private void SetupSignalingEvents()
        {
            _signaling.IncomingCallReceived += async (fromNumber) =>
            {
                try { await HandleIncomingCall(fromNumber); }
                catch (Exception ex) { _logger.LogError(ex, "着信処理中にエラー"); }
            };
            _signaling.AnswerReceived       += async () =>
            {
                try { await HandleAnswered(); }
                catch (Exception ex) { _logger.LogError(ex, "応答処理中にエラー"); }
            };
            _signaling.HangupReceived       += async () =>
            {
                try { await EndCallInternal(sendSignal: false, playSound: true); }
                catch (Exception ex) { _logger.LogError(ex, "切断処理中にエラー"); }
            };
            _signaling.CancelReceived       += async () =>
            {
                try
                {
                    if (CurrentStatus == PhoneStatus.Incoming)
                        await EndCallInternal(sendSignal: false, playSound: false);
                }
                catch (Exception ex) { _logger.LogError(ex, "キャンセル処理中にエラー"); }
            };
            _signaling.RejectReceived       += async () =>
            {
                try { await HandleRejected(); }
                catch (Exception ex) { _logger.LogError(ex, "拒否処理中にエラー"); }
            };
            _signaling.HoldReceived         += ()                 => HandleRemoteHold(true);
            _signaling.ResumeReceived       += ()                 => HandleRemoteHold(false);
            _signaling.ConnectionLost       += ()                 => { IsOnline = false; OnlineStateChanged?.Invoke(false); };
            _signaling.Reconnected          += async () =>
            {
                try { await _signaling.SendLogin(CurrentStation!.Number); }
                catch (Exception ex) { _logger.LogError(ex, "再接続ログイン中にエラー"); }
                IsOnline = true;
                OnlineStateChanged?.Invoke(true);
            };
        }

        // --- ステーション・オーディオ設定 ---

        public async Task ChangeStation(PhoneBookEntry newStation)
        {
            CurrentStation = newStation;
            if (_signaling.IsConnected)
            {
                await _signaling.SendLogin(newStation.Number);
            }
            OnlineStateChanged?.Invoke(IsOnline);
        }

        public void UpdateAudioDevices(DeviceInfo? input, DeviceInfo? normalOut, DeviceInfo? speakerOut = null)
        {
            _currentInputDevice = input;
            _normalOutputDevice = normalOut;
            _speakerOutputDevice = speakerOut;
            _sound.SetOutputDevice(normalOut?.ID);
        }

        // --- 通話操作（公開） ---

        public async Task StartCall(string targetNumber)
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
                    await EndCallInternal(sendSignal: false, playSound: false);
                return;
            }

            CurrentStatus = PhoneStatus.Outgoing;
            StatusChanged?.Invoke(CurrentStatus);

            _sound.Play(SoundName.Tori);
            await Task.Delay(800);

            if (CurrentStatus == PhoneStatus.Outgoing)
            {
                var response = await _signaling.SendCall(targetNumber);
                if (!response.IsConnected)
                {
                    await HandleBusySignal();
                    return;
                }
                _sound.Play(SoundName.Yobidashi, loop: true, loopIntervalMs: 2000);
            }
        }

        public async Task AnswerCall()
        {
            _sound.Stop();
            _sound.Play(SoundName.Tori);
            await _signaling.SendAnswer();
            await StartVoiceTransmission();

            CurrentStatus  = PhoneStatus.Talking;
            CallStartTime  = DateTime.Now;
            IsMuted        = false;
            IsSpeakerOn    = false;
            IsMyHold       = false;
            _isRemoteHold  = false;
            IsHolding      = false;
            StatusChanged?.Invoke(CurrentStatus);
        }

        public async Task EndCall() => await EndCallInternal(sendSignal: true, playSound: true);

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

        public async Task ToggleHold()
        {
            if (_isRemoteHold) return;
            IsMyHold = !IsMyHold;
            if (IsMyHold)
            {
                await _signaling.SendHold();
                StartHoldState(self: true);
            }
            else
            {
                await _signaling.SendResume();
                StopHoldState();
            }
        }

        // --- 内部ハンドラ ---

        private async Task HandleIncomingCall(string fromNumber)
        {
            await _stateLock.WaitAsync();
            try
            {
                if (CurrentStatus != PhoneStatus.Idle)
                {
                    return;
                }

                _connectedTargetNumber = fromNumber;
                ConnectedTargetName    = _phoneBookRepo.FindByNumber(fromNumber)?.Name ?? "不明";

                CurrentStatus = PhoneStatus.Incoming;
                StatusChanged?.Invoke(CurrentStatus);

                _sound.Play(SoundName.Yobi1, loop: true, loopIntervalMs: 1000);
                IncomingCallReceived?.Invoke(ConnectedTargetName!, fromNumber);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async Task HandleAnswered()
        {
            if (CurrentStatus != PhoneStatus.Outgoing) return;

            _sound.Stop();
            await StartVoiceTransmission();

            CurrentStatus  = PhoneStatus.Talking;
            CallStartTime  = DateTime.Now;
            IsMuted        = false;
            IsSpeakerOn    = false;
            IsMyHold       = false;
            _isRemoteHold  = false;
            IsHolding      = false;
            StatusChanged?.Invoke(CurrentStatus);
        }

        private async Task HandleBusySignal()
        {
            await _stateLock.WaitAsync();
            try
            {
                if (CurrentStatus != PhoneStatus.Outgoing) return;
                ConnectedTargetName = "話中";
                _sound.Play(SoundName.Watyu);
                StatusChanged?.Invoke(CurrentStatus);
            }
            finally
            {
                _stateLock.Release();
            }

            await Task.Delay(5000);

            await _stateLock.WaitAsync();
            try
            {
                if (CurrentStatus == PhoneStatus.Outgoing)
                    await EndCallInternal(sendSignal: false, playSound: false);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async Task HandleRejected()
        {
            await _stateLock.WaitAsync();
            try
            {
                if (CurrentStatus != PhoneStatus.Outgoing) return;
                ConnectedTargetName = "事情によりお繋ぎできません";
                _sound.Stop();
                _sound.SetOutputDevice(_normalOutputDevice?.ID);
                _sound.Play(SoundName.Watyu);
                StatusChanged?.Invoke(CurrentStatus);
            }
            finally
            {
                _stateLock.Release();
            }

            await Task.Delay(5000);

            await _stateLock.WaitAsync();
            try
            {
                if (CurrentStatus == PhoneStatus.Outgoing)
                    await EndCallInternal(sendSignal: false, playSound: false);
            }
            finally
            {
                _stateLock.Release();
            }
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

        private async Task StartVoiceTransmission()
        {
            int inDev = -1, outDevId = -1;
            if (_currentInputDevice != null)  int.TryParse(_currentInputDevice.ID,  out inDev);
            if (_normalOutputDevice != null)  int.TryParse(_normalOutputDevice.ID,  out outDevId);
            await _voice.StartTransmission(inDev, outDevId);
        }

        private async Task EndCallInternal(bool sendSignal, bool playSound)
        {
            if (sendSignal)
            {
                if (CurrentStatus == PhoneStatus.Incoming)
                    await _signaling.SendReject();
                else
                    await _signaling.SendHangup();
            }

            await _voice.StopTransmission();

            _connectedTargetNumber = null;
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
            DisposeAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await _voice.StopTransmission();
            _sound.Dispose();
            if (_signaling is IAsyncDisposable asyncSignaling)
                await asyncSignaling.DisposeAsync();
            else
                _signaling.Dispose();
        }
    }
}
