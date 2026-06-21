namespace TunarrDummyStart;

public enum ChannelRunState
{
    Idle,
    Connecting,
    Connected,
    Retrying,
    StartFailed,
    UnexpectedExit,
    Canceled,
    Stopped,
    Disabled
}

public sealed record ChannelStatusUpdate(
    int Channel,
    ChannelRunState State,
    int Attempt,
    int MaxAttempts,
    string Message,
    int? ExitCode,
    bool ConnectionEstablished,
    TimeSpan RunDuration,
    DateTimeOffset UpdatedAt);