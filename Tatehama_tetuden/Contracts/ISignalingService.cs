namespace Tatehama_tetuden.Contracts;

public interface ISignalingService : IDisposable, IAsyncDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(string? accessToken = null);
    Task SendLogin(string myNumber);
    Task SendCall(string targetNumber);
    Task SendAnswer(string targetNumber, string callerId);
    Task SendReject(string callerId);
    Task SendHangup(string targetId);
    Task SendBusy(string callerId);
    Task SendHold(string targetId);
    Task SendResume(string targetId);

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
