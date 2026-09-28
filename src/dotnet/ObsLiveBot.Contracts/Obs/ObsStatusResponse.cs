namespace ObsLiveBot.Contracts.Obs;

public sealed record ObsStatusResponse(
    string Connection,
    string? ObsVersion,
    string? WebSocketVersion,
    string? CurrentProgramScene,
    bool Streaming,
    bool Recording,
    bool RecordingPaused,
    DateTimeOffset LastUpdatedUtc);
