using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed class AudioArtifactLeaseRegistry : IAudioArtifactLeaseRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _leases = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public IDisposable Acquire(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_gate)
        {
            _leases.TryGetValue(fullPath, out var count);
            _leases[fullPath] = count + 1;
        }

        return new Lease(this, fullPath);
    }

    public bool IsLeased(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_gate) return _leases.ContainsKey(fullPath);
    }

    private void Release(string path)
    {
        lock (_gate)
        {
            if (!_leases.TryGetValue(path, out var count)) return;
            if (count <= 1) _leases.Remove(path);
            else _leases[path] = count - 1;
        }
    }

    private sealed class Lease(AudioArtifactLeaseRegistry owner, string path) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(path);
        }
    }
}

public sealed class NarrationArtifactValidator(TtsAudioStore audioStore) : INarrationArtifactValidator
{
    public NarrationArtifactValidation Validate(NarrationAudioArtifact artifact, string allowedRuntimeDirectory) =>
        audioStore.ValidateArtifact(artifact, allowedRuntimeDirectory);
}
