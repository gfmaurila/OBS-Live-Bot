namespace ObsLiveBot.Domain.Obs;

public enum ObsConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthenticationFailed,
    Faulted
}
