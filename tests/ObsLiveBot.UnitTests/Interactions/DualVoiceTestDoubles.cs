using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.UnitTests.Interactions;

/// <summary>
/// Captures the dual voice groups the orchestrator submits, in submission order, without touching a
/// real narrator. Used to assert the ordering contract and the autoplay gate.
/// </summary>
internal sealed class RecordingDualVoiceCoordinator : IDualVoiceNarrationCoordinator
{
    private readonly object _gate = new();
    private readonly List<DualVoiceNarrationRequest> _requests = [];

    public IReadOnlyList<DualVoiceNarrationRequest> Requests
    {
        get { lock (_gate) return _requests.ToArray(); }
    }

    public int Count
    {
        get { lock (_gate) return _requests.Count; }
    }

    public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken)
    {
        lock (_gate) _requests.Add(request);
        return Task.CompletedTask;
    }

    // The automatic path is the only one the orchestrator may reach, so the escape hatch is recorded
    // separately: a test asserting on Requests must never accidentally match a development test.
    public Task SubmitDevelopmentTestAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken) =>
        SubmitAsync(request, cancellationToken);
}

/// <summary>A coordinator that records submissions and can be made to fail, to test failure isolation.</summary>
internal sealed class FailingDualVoiceCoordinator : IDualVoiceNarrationCoordinator
{
    public int Attempts { get; private set; }

    public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken)
    {
        Attempts++;
        throw new InvalidOperationException("coordinator unavailable");
    }

    public Task SubmitDevelopmentTestAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken) =>
        SubmitAsync(request, cancellationToken);
}

/// <summary>A coordinator that never completes, used to prove the orchestrator does not block on it.</summary>
internal sealed class BlockingDualVoiceCoordinator : IDualVoiceNarrationCoordinator
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Attempts { get; private set; }

    public Task SubmitAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken)
    {
        Attempts++;
        return _gate.Task;
    }

    public Task SubmitDevelopmentTestAsync(DualVoiceNarrationRequest request, CancellationToken cancellationToken) =>
        SubmitAsync(request, cancellationToken);
}
