using Tatehama_tetuden.Models;

namespace Tatehama_tetuden.Repositories;

public interface ISignalingRepository : IDisposable, IAsyncDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync();
    Task SendLogin(string myNumber);
    Task<CallResponse> SendCall(string targetNumber);
    Task SendAnswer();
    Task SendReject();
    Task SendHangup();
    Task SendHold();
    Task SendResume();

    event Action<string>  IncomingCallReceived;
    event Action          AnswerReceived;
    event Action          HangupReceived;
    event Action          CancelReceived;
    event Action          RejectReceived;
    event Action          HoldReceived;
    event Action          ResumeReceived;
    event Action          ConnectionLost;
    event Action          Reconnecting;
    event Action          Reconnected;
}
