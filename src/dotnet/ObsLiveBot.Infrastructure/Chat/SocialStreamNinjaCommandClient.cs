using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Chat;

public sealed record SocialStreamNinjaSourceInfo(
    string Id,
    string Target,
    string? Username,
    string? VideoId,
    string? ConnectionMode,
    string? ActiveConnectionMode,
    bool Active,
    string? Status);

/// <summary>
/// Narrow, side-effect-explicit wrapper over the SSN command API.
/// Only the source lifecycle verbs used by StudioOS are exposed; nothing here reads or returns secrets.
/// </summary>
public sealed class SocialStreamNinjaCommandClient(HttpClient client, ILogger<SocialStreamNinjaCommandClient> logger)
{
    public async Task<IReadOnlyList<SocialStreamNinjaSourceInfo>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        using var document = await SendCommandAsync("getSources", new { }, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("payload", out var payload)) return [];
        var array = payload.ValueKind == JsonValueKind.Array
            ? payload
            : payload.TryGetProperty("sources", out var sources) ? sources : default;
        if (array.ValueKind != JsonValueKind.Array) return [];

        var result = new List<SocialStreamNinjaSourceInfo>();
        foreach (var item in array.EnumerateArray())
        {
            var id = ReadString(item, "id") ?? ReadString(item, "sourceId");
            var target = ReadString(item, "target");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target)) continue;
            var status = ReadString(item, "status") ?? ReadString(item, "state");
            var active = ReadBoolean(item, "active") || ReadBoolean(item, "isActive") ||
                         string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
            result.Add(new SocialStreamNinjaSourceInfo(
                id,
                target,
                ReadString(item, "username") ?? ReadString(item, "channel") ?? ReadString(item, "value"),
                ReadString(item, "videoId"),
                ReadString(item, "connectionMode"),
                ReadString(item, "activeConnectionMode"),
                active,
                status));
        }
        return result;
    }

    /// <summary>
    /// Creates the canonical per-live YouTube source. The idempotency key is derived from the
    /// videoId, so the same live never produces a second source.
    /// </summary>
    public async Task<SocialStreamNinjaSourceInfo?> AddYouTubeLiveChatSourceAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        if (!YouTubeLiveSourceIdentity.TryCreateLocator(videoId, out var locator))
            throw new ArgumentException("Invalid YouTube video ID.", nameof(videoId));

        using var document = await SendCommandAsync("addSource", new
        {
            target = "youtube",
            url = locator!.Url,
            videoId = locator.VideoId,
            idempotencyKey = locator.IdempotencyKey
        }, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("payload", out var payload) ||
            !payload.TryGetProperty("source", out var source))
            return null;
        var id = ReadString(source, "id");
        return string.IsNullOrWhiteSpace(id)
            ? null
            : new SocialStreamNinjaSourceInfo(
                id,
                ReadString(source, "target") ?? "youtube",
                ReadString(source, "username"),
                ReadString(source, "videoId") ?? locator.VideoId,
                ReadString(source, "connectionMode"),
                ReadString(source, "activeConnectionMode"),
                ReadBoolean(source, "active") ||
                string.Equals(ReadString(source, "status"), "active", StringComparison.OrdinalIgnoreCase),
                ReadString(source, "status"));
    }

    public Task StartSourceAsync(string sourceId, CancellationToken cancellationToken) =>
        SendCommandAsync("startSource", new { sourceId }, cancellationToken);

    public Task StopSourceAsync(string sourceId, CancellationToken cancellationToken) =>
        SendCommandAsync("stopSource", new { sourceId }, cancellationToken);

    public Task UpdateSettingsAsync(object settings, CancellationToken cancellationToken) =>
        SendCommandAsync("updateSettings", new { settings }, cancellationToken);

    private async Task<JsonDocument> SendCommandAsync(string action, object value, CancellationToken cancellationToken)
    {
        using var response = await client
            .PostAsJsonAsync("api/v1/command", new { action, value }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            document.Dispose();
            logger.LogWarning("SOCIALSTREAM_COMMAND_REJECTED action={Action}", action);
            throw new InvalidOperationException("SSApp rejected a source command.");
        }
        return document;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
