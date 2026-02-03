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
