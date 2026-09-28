using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Interactions;

public sealed class InteractionBuffer(IOptions<InteractionOptions> options) : IInteractionBuffer
{
    private readonly object _gate = new();
    private readonly Queue<InteractionResult> _results = new(options.Value.BufferCapacity);
    private long _total;
    private long _ignored;
    private long _completed;
    private long _failed;
    private DateTimeOffset? _lastInteractionAtUtc;

    public int Capacity { get; } = options.Value.BufferCapacity;

    public int Count
    {
        get { lock (_gate) return _results.Count; }
    }

    public void Add(InteractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            if (_results.Count == Capacity)
            {
                _results.Dequeue();
            }

            _results.Enqueue(result);
            _total++;
            _lastInteractionAtUtc = result.CompletedAtUtc;
            switch (result.Status)
            {
                case InteractionStatus.Ignored: _ignored++; break;
                case InteractionStatus.Completed: _completed++; break;
                case InteractionStatus.Failed: _failed++; break;
            }
        }
    }

    public IReadOnlyList<InteractionResult> GetRecent(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        lock (_gate)
        {
            return _results.Reverse().Take(safeLimit).ToArray();
        }
    }

    public InteractionStateSnapshot GetState(
        bool enabled,
        bool aiAvailable,
        bool ttsAvailable,
        string aiProvider,
        string aiStatus,
        string? aiModel,
        string ttsProvider,
        string ttsStatus,
        string? ttsVoice,
        string? ttsAudioFormat,
        int cooldownEntries,
        int cooldownCapacity)
    {
        lock (_gate)
        {
            var status = !enabled ? "Unavailable" : aiAvailable && ttsAvailable ? "Ready" : "Degraded";
            return new InteractionStateSnapshot(
                status,
                _total,
                _ignored,
                _completed,
                _failed,
                _results.Count,
                Capacity,
                cooldownEntries,
                cooldownCapacity,
                _lastInteractionAtUtc,
                aiProvider,
                aiStatus,
                aiModel,
                ttsProvider,
                ttsStatus,
                ttsVoice,
                ttsAudioFormat);
        }
    }
}
