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
    private readonly CallService               _sut;

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

    // ─── 初期状態 ────────────────────────────────────────────

    [Fact]
    public void 初期状態はIdle()
    {
        Assert.Equal(PhoneStatus.Idle, _sut.CurrentStatus);
    }

    // ─── 発信 ────────────────────────────────────────────────

    [Fact]
    public void StartCall_接続済みの場合_Outgoingに遷移し_SendCallが呼ばれる()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);
        var statusEvents = CaptureStatusEvents();

        _sut.StartCall("101");

        Assert.Contains(PhoneStatus.Outgoing, statusEvents);
        // async void 内の Task.Delay(800) の後に SendCall が呼ばれる
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

    // ─── 着信 ────────────────────────────────────────────────

    [Fact]
    public void 着信_Idleの場合_Incomingに遷移し_着信音が再生される()
    {
        var statusEvents = CaptureStatusEvents();

        SimulateIncomingCall("101", "caller-abc");

        Assert.Contains(PhoneStatus.Incoming, statusEvents);
        _mockSound.Verify(s => s.Play(SoundName.Yobi1, true, 1000), Times.Once);
    }

    [Fact]
    public void 着信_通話中の場合_Busyが返される()
    {
        SetupTalkingState();

        SimulateIncomingCall("102", "caller-xyz");

        _mockSignaling.Verify(s => s.SendBusy("caller-xyz"), Times.Once);
        Assert.Equal(PhoneStatus.Talking, _sut.CurrentStatus);
    }

    // ─── 受話 ────────────────────────────────────────────────

    [Fact]
    public void AnswerCall_着信中_Talkingに遷移し_音声開始される()
    {
        SimulateIncomingCall("101", "caller-abc");
        var statusEvents = CaptureStatusEvents();

        _sut.AnswerCall();

        Assert.Contains(PhoneStatus.Talking, statusEvents);
        _mockVoice.Verify(v => v.StartTransmission(
            It.IsAny<string>(), "caller-abc",
            It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    // ─── 切断 ────────────────────────────────────────────────

    [Fact]
    public void EndCall_通話中_Hangupが送られ_Idleに戻る()
    {
        SetupTalkingState();
        var statusEvents = CaptureStatusEvents();

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

    // ─── ミュート・スピーカー・保留 ────────────────────────

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
        _sut.ToggleSpeaker();
        Assert.False(_sut.IsSpeakerOn);
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
        _sut.ToggleHold(); // → 自己保留

        _sut.ToggleHold(); // → 再開

        Assert.False(_sut.IsMyHold);
        Assert.False(_sut.IsHolding);
        _mockSignaling.Verify(s => s.SendResume(It.IsAny<string>()), Times.Once);
    }

    // ─── 電話帳検索 ──────────────────────────────────────────

    [Fact]
    public void StartCall_登録番号_名前が正しくセットされる()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);

        _sut.StartCall("101");

        Assert.Equal("総合司令所 館浜司令", _sut.ConnectedTargetName);
    }

    [Fact]
    public void StartCall_未登録番号_未登録メッセージが表示される()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);

        _sut.StartCall("999");

        Assert.Equal("未登録", _sut.ConnectedTargetName);
    }

    // ─── ヘルパー ────────────────────────────────────────────

    /// <summary>IncomingCallReceived イベントを発火させる</summary>
    private void SimulateIncomingCall(string fromNumber, string callerId)
    {
        _mockSignaling.Raise(s => s.IncomingCallReceived += null, fromNumber, callerId);
    }

    /// <summary>StatusChanged イベントを記録するリストを返す</summary>
    private List<PhoneStatus> CaptureStatusEvents()
    {
        var events = new List<PhoneStatus>();
        _sut.StatusChanged += (status) => events.Add(status);
        return events;
    }

    /// <summary>通話中状態にセットする</summary>
    private void SetupTalkingState()
    {
        _mockSignaling.SetupGet(s => s.IsConnected).Returns(true);
        SimulateIncomingCall("101", "caller-abc");
        _sut.AnswerCall();
    }
}
