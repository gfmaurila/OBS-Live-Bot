using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Interactions;

/// <summary>A Piper voice that is present on disk and may be selected by a request.</summary>
public sealed record PiperVoiceModel(string VoiceId, string ModelPath, string ConfigPath);

/// <summary>
/// Resolves a requested voice id to a Piper model that actually exists on disk.
///
/// The catalog is deliberately closed. A request names a voice, and the only way to satisfy it is to
/// find that exact <c>&lt;voiceId&gt;.onnx</c> plus its <c>.json</c> config inside the configured voices
/// directory. A voice id containing separators, parent references, or an extension is rejected rather
/// than normalized, so this can never be turned into a path that escapes the directory or selects a
/// file the operator did not intend to expose.
/// </summary>
public sealed class PiperVoiceCatalog
{
    private readonly PiperTtsOptions _options;
    private readonly ILogger<PiperVoiceCatalog> _logger;

    public PiperVoiceCatalog(
        IOptions<InteractionOptions> options,
        ILogger<PiperVoiceCatalog> logger)
    {
        _options = options.Value.Tts;
        _logger = logger;
    }

    public string DefaultVoice => _options.Voice;

    /// <summary>The configured default voice's model, used for availability checks and as fallback.</summary>
    public string DefaultModelPath => _options.ModelPath;

    /// <summary>
    /// Returns the model for <paramref name="requestedVoiceId"/>, or null when that voice is not
    /// available. An empty or whitespace id resolves to the configured default voice.
    /// </summary>
    public PiperVoiceModel? Resolve(string? requestedVoiceId)
    {
        var voiceId = string.IsNullOrWhiteSpace(requestedVoiceId) ? _options.Voice : requestedVoiceId.Trim();
        if (IsUnsafeVoiceId(voiceId))
        {
            _logger.LogWarning(
                "TTS_VOICE_REJECTED voiceId={VoiceId} reason=UNSAFE_VOICE_ID",
                voiceId);
            return null;
        }

        var directory = VoicesDirectory;
        if (directory is null) return null;

        var modelPath = Path.GetFullPath(Path.Combine(directory, voiceId + ".onnx"));
        var configPath = modelPath + ".json";
        if (!IsInsideVoicesDirectory(modelPath) || !IsInsideVoicesDirectory(configPath))
        {
            _logger.LogWarning(
                "TTS_VOICE_REJECTED voiceId={VoiceId} reason=OUTSIDE_VOICES_DIRECTORY",
                voiceId);
            return null;
        }

        if (!File.Exists(modelPath) || !File.Exists(configPath))
        {
            _logger.LogWarning(
                "TTS_VOICE_UNAVAILABLE voiceId={VoiceId} reason=MODEL_NOT_FOUND",
                voiceId);
            return null;
        }

        return new PiperVoiceModel(voiceId, modelPath, configPath);
    }

    /// <summary>Enumerates the voices that are actually installed, for diagnostics and settings UIs.</summary>
    public IReadOnlyList<string> ListInstalled()
    {
        var directory = VoicesDirectory;
        if (directory is null || !Directory.Exists(directory)) return [];

        return new DirectoryInfo(directory)
            .EnumerateFiles("*.onnx", SearchOption.TopDirectoryOnly)
            .Where(file => File.Exists(file.FullName + ".json"))
            .Select(file => Path.GetFileNameWithoutExtension(file.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private string? VoicesDirectory
    {
        get
        {
            try
            {
                return Path.GetFullPath(_options.VoicesDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _logger.LogWarning("TTS_VOICES_DIRECTORY_INVALID errorType={ErrorType}", exception.GetType().Name);
                return null;
            }
        }
    }

    private bool IsInsideVoicesDirectory(string path)
    {
        var directory = VoicesDirectory;
        if (directory is null) return false;
        var rooted = directory.EndsWith(Path.DirectorySeparatorChar) ||
                     directory.EndsWith(Path.AltDirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(rooted, PathComparison);
    }

    /// <summary>
    /// A voice id is an identifier, not a path. Only characters that can appear in a Piper voice name
    /// are accepted, which removes traversal, absolute paths and double extensions at the source.
    /// </summary>
    private static bool IsUnsafeVoiceId(string voiceId)
    {
        if (voiceId.Length is 0 or > 64) return true;
        if (voiceId.Contains(Path.DirectorySeparatorChar) ||
            voiceId.Contains(Path.AltDirectorySeparatorChar) ||
            voiceId.Contains("..", StringComparison.Ordinal) ||
            voiceId.Contains(':') ||
            voiceId.Contains('.'))
        {
            return true;
        }

        foreach (var character in voiceId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                return true;
            }
        }

        return false;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
