using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class NarrationOptionsValidator(
    IOptions<InteractionOptions> interactionOptions) : IValidateOptions<NarrationOptions>
{
    private static readonly string[] AllowedTemplateTokens = ["{username}", "{message}"];

    public ValidateOptionsResult Validate(string? name, NarrationOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SourceName)) failures.Add("Narration:SourceName is required.");
        if (options.MaxQueueSize is < 1 or > 100) failures.Add("Narration:MaxQueueSize must be between 1 and 100.");
        if (options.MaxConcurrentPlayback != 1) failures.Add("Narration:MaxConcurrentPlayback must be 1.");
        if (options.MaxNarrationSeconds is < 1 or > 120) failures.Add("Narration:MaxNarrationSeconds must be between 1 and 120.");
        if (options.PlaybackTimeoutSeconds < options.MaxNarrationSeconds || options.PlaybackTimeoutSeconds > 180)
            failures.Add("Narration:PlaybackTimeoutSeconds must cover the maximum narration duration and be at most 180.");
        if (options.StartTimeoutSeconds is < 1 or > 60)
            failures.Add("Narration:StartTimeoutSeconds must be between 1 and 60.");
        if (options.PollIntervalMilliseconds is < 25 or > 2_000)
            failures.Add("Narration:PollIntervalMilliseconds must be between 25 and 2000.");
        if (!double.IsFinite(options.MinimumVolume) || !double.IsFinite(options.MaximumVolume) ||
            options.MinimumVolume < 0 || options.MaximumVolume > 100 || options.MinimumVolume >= options.MaximumVolume)
            failures.Add("Narration volume range must be finite, within 0–100, and have min < max.");
        if (!double.IsFinite(options.DefaultVolume) || options.DefaultVolume < options.MinimumVolume ||
            options.DefaultVolume > options.MaximumVolume)
            failures.Add("Narration:DefaultVolume must be within the configured volume range.");
        if (!string.Equals(options.MonitoringMode, "MonitorOff", StringComparison.Ordinal))
            failures.Add("Narration:MonitoringMode must be MonitorOff to prevent duplicated monitoring/feedback.");
        if (string.IsNullOrWhiteSpace(options.AllowedRuntimeDirectory) ||
            string.IsNullOrWhiteSpace(interactionOptions.Value.Tts.OutputDirectory) ||
            !PathEquals(options.AllowedRuntimeDirectory, interactionOptions.Value.Tts.OutputDirectory))
            failures.Add("Narration:AllowedRuntimeDirectory must match Interactions:Tts:OutputDirectory.");
        if (string.IsNullOrWhiteSpace(options.HostRuntimeDirectory) || !IsWindowsAbsolutePath(options.HostRuntimeDirectory))
            failures.Add("Narration:HostRuntimeDirectory must be an absolute Windows local path.");
        if (options.EventBufferCapacity is < 1 or > 10_000)
            failures.Add("Narration:EventBufferCapacity must be between 1 and 10000.");

        // How long an accepted interaction may hold its place in the playback order waiting for its own
        // audio. It bounds the worst case: below the slowest model call plus synthesis, a busy stream
        // would have healthy interactions force-released; far above it, a hung call stalls the order.
        if (options.GroupAdmissionTimeoutSeconds is < 5 or > 600)
            failures.Add("Narration:GroupAdmissionTimeoutSeconds must be between 5 and 600.");

        // Dual voice roles. The username template is substituted literally when speaking, so an
        // unrecognized token is a startup error rather than raw punctuation read aloud mid-stream.
        ValidateRole(failures, "ChatVoice", options.ChatVoice);
        ValidateRole(failures, "AssistantVoice", options.AssistantVoice);
        if (options.ChatVoice is not null)
        {
            if (options.ChatVoice.SpeakUserName && string.IsNullOrWhiteSpace(options.ChatVoice.UserNameFormat))
                failures.Add("Narration:ChatVoice:UserNameFormat is required when SpeakUserName is true.");
            if (options.ChatVoice.SpeakUserName &&
                !HasOnlyKnownTokens(options.ChatVoice.UserNameFormat ?? string.Empty))
                failures.Add("Narration:ChatVoice:UserNameFormat may only use the {username} and {message} tokens.");
            if (options.ChatVoice.MaxMessageCharacters is < 1 or > 2_000)
                failures.Add("Narration:ChatVoice:MaxMessageCharacters must be between 1 and 2000.");
            if (string.IsNullOrWhiteSpace(options.ChatVoice.UrlSpokenWord))
                failures.Add("Narration:ChatVoice:UrlSpokenWord is required.");
            else if (options.ChatVoice.UrlSpokenWord.Any(char.IsControl))
                failures.Add("Narration:ChatVoice:UrlSpokenWord must not contain control characters.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRole(
        List<string> failures,
        string role,
        NarrationVoiceRoleOptions? options)
    {
        if (options is null)
        {
            failures.Add($"Narration:{role} is required.");
            return;
        }

        if (options.Volume is { } volume &&
            (!double.IsFinite(volume) || volume is < 0 or > 100))
            failures.Add($"Narration:{role}:Volume must be between 0 and 100 when set.");
        if (options.VoiceId is { Length: > 64 } || options.VoiceId?.Any(char.IsControl) == true)
            failures.Add($"Narration:{role}:VoiceId must be at most 64 characters with no control characters.");
        if (options.VoiceId is not null &&
            (options.VoiceId.Contains('/') || options.VoiceId.Contains('\\') ||
             options.VoiceId.Contains("..", StringComparison.Ordinal) ||
             options.VoiceId.Contains('.')))
            failures.Add($"Narration:{role}:VoiceId must be a voice name, not a path.");
    }

    private static bool HasOnlyKnownTokens(string format)
    {
        for (var index = 0; index < format.Length; index++)
        {
            var character = format[index];
            if (character == '{')
            {
                var close = format.IndexOf('}', index);
                if (close < 0) return false;
                var token = format[index..(close + 1)];
                if (!AllowedTemplateTokens.Contains(token, StringComparer.Ordinal)) return false;
                index = close;
            }
            else if (character == '}')
            {
                // A closing brace with no supported opening token in front of it.
                return false;
            }
        }

        return true;
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException) { return false; }
    }

    private static bool IsWindowsAbsolutePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' &&
        path[2] is '/' or '\\';
}
