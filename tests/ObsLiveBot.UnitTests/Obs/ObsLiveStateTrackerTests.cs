using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Obs.GetEvents;
using ObsLiveBot.Application.Features.Obs.GetLiveState;
using ObsLiveBot.Application.LiveState;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.UnitTests.Obs;

public sealed class ObsLiveStateTrackerTests
{
    [Fact]
    public async Task InitialSynchronization_PopulatesCompleteState()
    {
        var tracker = CreateTracker();
        await SynchronizeAsync(tracker);

        Assert.True(tracker.State.IsSynchronized);
        Assert.False(tracker.State.IsStale);
        Assert.Equal("Program", tracker.State.CurrentProgramScene);
        Assert.Equal("Collection", tracker.State.CurrentSceneCollection);
        Assert.Equal("Profile", tracker.State.CurrentProfile);
        Assert.Equal(ObsReplayBufferState.Stopped, tracker.State.ReplayBufferState);
        Assert.Equal(ObsVirtualCameraState.Stopped, tracker.State.VirtualCameraState);
    }

    [Fact]
    public async Task SceneChanged_RecordsPreviousAndCurrentScene()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("CurrentProgramSceneChanged", """{"sceneName":"Gameplay"}"""), CancellationToken.None);

        Assert.Equal("Gameplay", tracker.State.CurrentProgramScene);
        var envelope = Assert.Single(tracker.GetRecentEvents(1));
        Assert.Equal("ObsSceneChanged", envelope.EventType);
        Assert.Equal("Program", envelope.Payload["previousScene"]);
    }

    [Fact]
    public async Task DuplicateSceneEvent_IsDeduplicated()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        var before = tracker.GetRecentEvents(100).Count;
        await tracker.ProcessAsync(Event("CurrentProgramSceneChanged", """{"sceneName":"Program"}"""), CancellationToken.None);
        Assert.Equal(before, tracker.GetRecentEvents(100).Count);
    }

    [Theory]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STARTING", ObsStreamState.Starting)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STARTED", ObsStreamState.Live)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STOPPING", ObsStreamState.Stopping)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STOPPED", ObsStreamState.Offline)]
    public async Task StreamTransitions_AreMapped(string outputState, ObsStreamState expected)
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        if (expected == ObsStreamState.Offline)
            await tracker.ProcessAsync(Event("StreamStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        await tracker.ProcessAsync(Event("StreamStateChanged", Output(outputState)), CancellationToken.None);
        Assert.Equal(expected, tracker.State.StreamState);
    }

    [Theory]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STARTING", ObsRecordingState.Starting)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STARTED", ObsRecordingState.Recording)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_PAUSED", ObsRecordingState.Paused)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_RESUMED", ObsRecordingState.Recording)]
    [InlineData("OBS_WEBSOCKET_OUTPUT_STOPPED", ObsRecordingState.Stopped)]
    public async Task RecordingTransitions_AreMapped(string outputState, ObsRecordingState expected)
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        if (expected == ObsRecordingState.Stopped)
            await tracker.ProcessAsync(Event("RecordStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        await tracker.ProcessAsync(Event("RecordStateChanged", Output(outputState)), CancellationToken.None);
        Assert.Equal(expected, tracker.State.RecordingState);
    }

    [Fact]
    public async Task ReplayBufferAndVirtualCamera_AreTracked()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("ReplayBufferStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        await tracker.ProcessAsync(Event("VirtualcamStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        Assert.Equal(ObsReplayBufferState.Running, tracker.State.ReplayBufferState);
        Assert.Equal(ObsVirtualCameraState.Active, tracker.State.VirtualCameraState);
    }

    [Fact]
    public async Task ProfileAndSceneCollectionChanges_AreTracked()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("CurrentProfileChanged", """{"profileName":"Streaming"}"""), CancellationToken.None);
        await tracker.ProcessAsync(Event("CurrentSceneCollectionChanged", """{"sceneCollectionName":"Live"}"""), CancellationToken.None);
        Assert.Equal("Streaming", tracker.State.CurrentProfile);
        Assert.Equal("Live", tracker.State.CurrentSceneCollection);
    }

    [Fact]
    public async Task InputMuteAndVolumeChanges_ArePublished()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("InputMuteStateChanged", """{"inputName":"Mic","inputMuted":true}"""), CancellationToken.None);
        await tracker.ProcessAsync(Event("InputVolumeChanged", """{"inputName":"Mic","inputVolumeDb":-12.5}"""), CancellationToken.None);
        Assert.Contains(tracker.GetRecentEvents(10), item => item.EventType == "ObsInputMuteChanged");
        Assert.Contains(tracker.GetRecentEvents(10), item => item.EventType == "ObsInputVolumeChanged");
    }

    [Fact]
    public async Task SourceAndVisibilityChanges_ArePublished()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("InputCreated", """{"inputName":"Camera"}"""), CancellationToken.None);
        await tracker.ProcessAsync(Event("SceneItemEnableStateChanged", """{"sceneName":"Program","sceneItemEnabled":false}"""), CancellationToken.None);
        Assert.Equal(2, tracker.GetRecentEvents(2).Count(item => item.EventType == "ObsSourceChanged"));
    }

    [Fact]
    public async Task Reconnect_MarksStaleThenResynchronizesWithNewConnection()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        var firstConnection = tracker.State.ConnectionId;
        await tracker.MarkStaleAsync(ObsConnectionState.Reconnecting, CancellationToken.None);
        Assert.True(tracker.State.IsStale);
        await SynchronizeAsync(tracker);
        Assert.False(tracker.State.IsStale);
        Assert.NotEqual(firstConnection, tracker.State.ConnectionId);
    }

    [Fact]
    public async Task Events_PreserveMonotonicOrdering()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("StreamStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTING")), CancellationToken.None);
        await tracker.ProcessAsync(Event("StreamStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        var ordered = tracker.GetRecentEvents(10).OrderBy(item => item.Sequence).ToArray();
        Assert.True(ordered.Zip(ordered.Skip(1), (a, b) => a.Sequence < b.Sequence).All(value => value));
    }

    [Fact]
    public async Task EquivalentStreamState_IsDeduplicated()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        await tracker.ProcessAsync(Event("StreamStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        var count = tracker.GetRecentEvents(100).Count;
        await tracker.ProcessAsync(Event("StreamStateChanged", Output("OBS_WEBSOCKET_OUTPUT_STARTED")), CancellationToken.None);
        Assert.Equal(count, tracker.GetRecentEvents(100).Count);
    }

    [Fact]
    public async Task RecentEventBuffer_IsBounded()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        for (var index = 0; index < 130; index++)
            await tracker.ProcessAsync(Event("InputMuteStateChanged", $$"""{"inputName":"Mic{{index}}","inputMuted":true}"""), CancellationToken.None);
        Assert.Equal(ObsLiveStateTracker.EventBufferCapacity, tracker.GetRecentEvents(1000).Count);
    }

    [Fact]
    public async Task LiveStateAndEventsQueries_ReturnSanitizedResponses()
    {
        var tracker = CreateTracker(); await SynchronizeAsync(tracker);
        var state = (await new GetObsLiveStateQueryHandler(tracker).Handle(new GetObsLiveStateQuery(), CancellationToken.None)).Value;
        var events = (await new GetRecentObsEventsQueryHandler(tracker).Handle(new GetRecentObsEventsQuery(20), CancellationToken.None)).Value;
        var json = JsonSerializer.Serialize(new { state, events });
        Assert.Equal("Program", state.Scene);
        Assert.NotEmpty(events);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    public async Task RecentEventsLimit_ValidatesSupportedRange(int limit, bool expectedValid)
    {
        var result = await new GetRecentObsEventsQueryValidator().ValidateAsync(new GetRecentObsEventsQuery(limit));

        Assert.Equal(expectedValid, result.IsValid);
    }

    private static ObsLiveStateTracker CreateTracker() => new(new CollectingPublisher(), new CollectingBridge(), TimeProvider.System, NullLogger<ObsLiveStateTracker>.Instance);

    private static Task SynchronizeAsync(ObsLiveStateTracker tracker) => tracker.SynchronizeAsync(
        new ObsStateSnapshot("32.1.2", "5.7.3", "Program", "Collection", "Profile", false, false, false, false, false),
        Guid.NewGuid(), CancellationToken.None);

    private static ObsExternalEvent Event(string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ObsExternalEvent(type, document.RootElement.Clone(), DateTimeOffset.UtcNow);
    }

    private static string Output(string state) => $$"""{"outputState":"{{state}}","outputActive":true}""";

    private sealed class CollectingPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
    }

    private sealed class CollectingBridge : ILiveEventPublisher
    {
        public Task PublishAsync(ObsEventEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
