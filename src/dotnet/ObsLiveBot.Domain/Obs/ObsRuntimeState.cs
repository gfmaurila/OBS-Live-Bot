namespace ObsLiveBot.Domain.Obs;

public sealed record ObsRuntimeState(
    ObsConnectionState ConnectionState,
    string? ObsVersion,
    string? WebSocketVersion,
    string? CurrentProgramScene,
    bool IsStreaming,
    bool IsRecording,
    bool IsRecordingPaused,
    DateTimeOffset LastUpdatedUtc)
{
    public static ObsRuntimeState Initial { get; } = new(
        ObsConnectionState.Disconnected,
        null,
        null,
        null,
        false,
        false,
        false,
        DateTimeOffset.UtcNow);
}
