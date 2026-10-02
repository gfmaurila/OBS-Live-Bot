using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Chat.Ingest;
using ObsLiveBot.Application.YouTube;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Chat;

public sealed class SocialStreamNinjaLiveChatProvider(
    IOptions<SocialStreamNinjaOptions> options,
    IOptions<YouTubeLiveDiscoveryOptions> liveDiscoveryOptions,
    IHttpClientFactory httpClientFactory,
    SocialStreamNinjaMessageMapper mapper,
    ISender sender,
    ILogger<SocialStreamNinjaLiveChatProvider> logger) : ILiveChatProvider
{
    private readonly object _gate = new();
    private readonly HashSet<string> _platformsObserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly SocialStreamNinjaOptions _options = options.Value;
    private readonly YouTubeLiveDiscoveryOptions _liveDiscovery = liveDiscoveryOptions.Value;
    private HttpResponseMessage? _activeStream;
    private LiveChatProviderSnapshot _snapshot = new(
        LiveChatProviderType.SocialStreamNinja,
        options.Value.Enabled,
        options.Value.Enabled ? LiveChatProviderState.Disconnected : LiveChatProviderState.Disabled,
        ConfiguredPlatforms(options.Value),
        null, null, null);

    public LiveChatProviderType Provider => LiveChatProviderType.SocialStreamNinja;
    public LiveChatProviderSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public TimeSpan? RetryAfter => null;
    public Task Completion => Task.CompletedTask;

    public void SetLifecycleState(LiveChatProviderState state, string? error = null) =>
        UpdateSnapshot(state: state, error: error);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            UpdateSnapshot(state: LiveChatProviderState.Disabled, processRunning: false, transportReady: false, captureReady: false);
            return;
        }

        var client = httpClientFactory.CreateClient("SocialStreamNinja");
        using (var readinessCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            readinessCancellation.CancelAfter(TimeSpan.FromSeconds(15));
            using var readiness = await client.GetAsync("api/v1/capabilities", readinessCancellation.Token).ConfigureAwait(false);
            readiness.EnsureSuccessStatusCode();
        }
        UpdateSnapshot(state: LiveChatProviderState.Connecting, processRunning: true, transportReady: false);
        logger.LogInformation("SOCIALSTREAM_PROCESS_READY");

        if (_options.YouTube.Enabled &&
            string.Equals(_options.YouTube.AuthMode, "oauth", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await ConfigureYouTubeAutoDiscoveryAsync(
                    client,
                    // SSN's own YouTube auto-add stays off when StudioOS owns the lifecycle,
                    // because its discovery is unreliable and creates sources nobody can reconcile.
                    enabled: !AutomaticDiscoveryOwnsYouTubeSource &&
                             string.IsNullOrWhiteSpace(_options.YouTube.LiveChatUrl),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "SOCIALSTREAM_YOUTUBE_AUTO_DISCOVERY_UNAVAILABLE errorType={ErrorType}",
                    exception.GetType().Name);
            }
        }
        var captureReady = !_options.ConfigureSources || await EnsureSourcesAsync(client, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/events");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        lock (_gate) _activeStream = response;
        UpdateSnapshot(
            state: LiveChatProviderState.Connected,
            error: captureReady ? null : "source_setup_partial",
            processRunning: true,
            transportReady: true,
            captureReady: captureReady,
            connectedAt: DateTimeOffset.UtcNow);
        logger.LogInformation("SOCIALSTREAM_TRANSPORT_READY transport={Transport}", "SSE");

        try
        {
            await ReadEventStreamAsync(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeStream, response)) _activeStream = null;
            }
            UpdateSnapshot(state: LiveChatProviderState.Disconnected, transportReady: false, captureReady: false);
            response.Dispose();
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage? response;
        lock (_gate)
        {
            response = _activeStream;
            _activeStream = null;
        }
        response?.Dispose();
        UpdateSnapshot(
            state: _options.Enabled ? LiveChatProviderState.Disconnected : LiveChatProviderState.Disabled,
            processRunning: false,
            transportReady: false,
            captureReady: false);
        logger.LogInformation("SOCIALSTREAM_PROVIDER_STOPPED");
        return Task.CompletedTask;
    }

    public async Task<LiveChatIngestionResult?> IngestFixtureAsync(
        JsonElement envelope,
        CancellationToken cancellationToken)
    {
        if (!mapper.TryReadEnvelope(envelope, out var captured, out var rejection))
        {
            logger.LogWarning("SOCIALSTREAM_PAYLOAD_REJECTED reason={Reason}", rejection);
            return null;
        }
        return await IngestCapturedAsync(captured!, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadEventStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: false);
        var eventName = string.Empty;
        var data = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return;
            if (line.Length == 0)
            {
                if (data.Length > 0 && (eventName.Length == 0 || eventName == "source.event"))
                    await ProcessSseDataAsync(data.ToString(), cancellationToken).ConfigureAwait(false);
                eventName = string.Empty;
                data.Clear();
                continue;
            }
            if (line.StartsWith(':')) continue;
            if (line.StartsWith("event:", StringComparison.Ordinal))
                eventName = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart());
                if (Encoding.UTF8.GetByteCount(data.ToString()) > _options.MaxEventBytes)
                {
                    logger.LogWarning("SOCIALSTREAM_PAYLOAD_REJECTED reason={Reason}", "payload_too_large");
                    data.Clear();
                    eventName = "rejected";
                }
            }
        }
    }

    private async Task ProcessSseDataAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (!mapper.TryReadEnvelope(document.RootElement, out var captured, out var rejection))
            {
                if (rejection != "non_chat_event")
                    logger.LogWarning("SOCIALSTREAM_PAYLOAD_REJECTED reason={Reason}", rejection);
                return;
            }
            await IngestCapturedAsync(captured!, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            logger.LogWarning("SOCIALSTREAM_PAYLOAD_REJECTED reason={Reason}", "malformed_json");
        }
    }

    private async Task<LiveChatIngestionResult?> IngestCapturedAsync(
        SocialStreamNinjaCapturedEvent captured,
        CancellationToken cancellationToken)
    {
        if (!mapper.TryMap(captured, out var mapped, out var rejection))
        {
            logger.LogWarning("SOCIALSTREAM_NORMALIZATION_REJECTED reason={Reason}", rejection);
            return null;
        }
        var result = await sender.Send(new IngestLiveChatEventCommand(mapped!), cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value.Accepted)
        {
            lock (_gate) _platformsObserved.Add(mapped!.Provider.ToString());
            UpdateSnapshot(lastEventAt: result.Value.Event?.ReceivedAtUtc, captureReady: true);
            logger.LogInformation(
                "SOCIALSTREAM_EVENT_NORMALIZED platform={Platform} eventType={EventType}",
                mapped!.Provider,
                mapped.EventType);
        }
        return result.Value;
    }

    private async Task<bool> EnsureSourcesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var desired = DesiredSources().ToArray();
        if (desired.Length == 0) return false;
        var allReady = true;
        foreach (var source in desired)
        {
            try
            {
                var sources = await GetSourcesAsync(client, cancellationToken).ConfigureAwait(false);
                if (string.Equals(source.Target, "youtube", StringComparison.OrdinalIgnoreCase) &&
                    YouTubeLiveChatSourceLocator.TryCreate(source.Channel, out var liveChatLocator))
                {
                    using var added = await SendCommandAsync(client, "addSource", new
                    {
                        target = "youtube",
                        url = liveChatLocator!.Url,
                        videoId = liveChatLocator.VideoId,
                        idempotencyKey = liveChatLocator.IdempotencyKey
                    }, cancellationToken).ConfigureAwait(false);
                    var canonicalId = added.RootElement.TryGetProperty("payload", out var addPayload) &&
                                      addPayload.TryGetProperty("source", out var addedSource)
                        ? ReadString(addedSource, "id")
                        : null;
                    if (string.IsNullOrWhiteSpace(canonicalId))
                    {
                        allReady = false;
                        logger.LogWarning("SOCIALSTREAM_SOURCE_SETUP_FAILED platform={Platform} reason={Reason}", source.Target, "live_chat_source_not_returned");
                        continue;
                    }

                    sources = await GetSourcesAsync(client, cancellationToken).ConfigureAwait(false);
                    var canonical = sources.FirstOrDefault(item => string.Equals(item.Id, canonicalId, StringComparison.Ordinal));
                    if (canonical is null)
                    {
                        allReady = false;
                        logger.LogWarning("SOCIALSTREAM_SOURCE_SETUP_FAILED platform={Platform} reason={Reason}", source.Target, "live_chat_source_not_listed");
                        continue;
                    }

                    foreach (var duplicate in sources.Where(item =>
                                 string.Equals(item.Target, "youtube", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(item.Id, canonical.Id, StringComparison.Ordinal) &&
                                 item.Active &&
                                 string.Equals(item.VideoId, liveChatLocator.VideoId, StringComparison.Ordinal)))
                    {
                        await SendCommandAsync(client, "stopSource", new { sourceId = duplicate.Id }, cancellationToken)
                            .ConfigureAwait(false);
                        logger.LogInformation("SOCIALSTREAM_SOURCE_STOPPED platform={Platform} reason={Reason}", "YouTube", "duplicate_live_video");
                    }

                    if (!canonical.Active)
                        await SendCommandAsync(client, "startSource", new { sourceId = canonical.Id }, cancellationToken)
                            .ConfigureAwait(false);
                    logger.LogInformation(
                        "SOCIALSTREAM_YOUTUBE_LIVE_CHAT_SOURCE_READY sourceId={SourceId} videoId={VideoId} mode={ConnectionMode}",
                        canonical.Id,
                        liveChatLocator.VideoId,
                        canonical.ConnectionMode ?? "classic");
                    continue;
                }

                var existing = sources.FirstOrDefault(item =>
                    string.Equals(item.Target, source.Target, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Username, source.Channel, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    await SendCommandAsync(client, "addSource", new
                    {
                        target = source.Target,
                        username = source.Channel
                    }, cancellationToken).ConfigureAwait(false);
                    sources = await GetSourcesAsync(client, cancellationToken).ConfigureAwait(false);
                    existing = sources.FirstOrDefault(item =>
                        string.Equals(item.Target, source.Target, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.Username, source.Channel, StringComparison.OrdinalIgnoreCase));
                }
                if (existing is null)
                {
                    allReady = false;
                    logger.LogWarning("SOCIALSTREAM_SOURCE_SETUP_FAILED platform={Platform} reason={Reason}", source.Target, "source_not_returned");
                    continue;
                }
                var preferredConnectionMode = PreferredSimpleConnectionMode(source.Target);
                if (preferredConnectionMode is not null &&
                    !string.Equals(existing.ConnectionMode, preferredConnectionMode, StringComparison.OrdinalIgnoreCase))
                {
                    if (existing.Active)
                        await SendCommandAsync(client, "stopSource", new { sourceId = existing.Id }, cancellationToken)
                            .ConfigureAwait(false);
                    await SendCommandAsync(client, "setSourceConnectionMode", new
                    {
                        sourceId = existing.Id,
                        mode = preferredConnectionMode
                    }, cancellationToken).ConfigureAwait(false);
                    existing = existing with { ConnectionMode = preferredConnectionMode, Active = false };
                    logger.LogInformation(
                        "SOCIALSTREAM_SOURCE_MODE_SET platform={Platform} mode={ConnectionMode}",
                        source.Target,
                        preferredConnectionMode);
                }
                if (!existing.Active)
                    await SendCommandAsync(client, "startSource", new { sourceId = existing.Id }, cancellationToken)
                        .ConfigureAwait(false);
                logger.LogInformation("SOCIALSTREAM_SOURCE_CONFIGURED platform={Platform}", source.Target);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                allReady = false;
                logger.LogWarning(
                    "SOCIALSTREAM_SOURCE_SETUP_FAILED platform={Platform} errorType={ErrorType}",
                    source.Target,
                    exception.GetType().Name);
            }
        }
        return allReady;
    }

    private IEnumerable<(string Target, string Channel)> DesiredSources()
    {
        if (_options.Twitch.Enabled && !string.IsNullOrWhiteSpace(_options.Twitch.Channel))
            yield return ("twitch", _options.Twitch.Channel);

        // When automatic discovery owns the YouTube lifecycle this provider must not create or
        // start a YouTube source: two owners racing on the same live is exactly how duplicates
        // appear. A manual per-live URL keeps the previous provider-managed behavior.
        if (!AutomaticDiscoveryOwnsYouTubeSource && _options.YouTube.Enabled &&
            !string.IsNullOrWhiteSpace(_options.YouTube.LiveChatUrl))
            yield return ("youtube", _options.YouTube.LiveChatUrl);
        else if (!AutomaticDiscoveryOwnsYouTubeSource && _options.YouTube.Enabled &&
                 !string.Equals(_options.YouTube.AuthMode, "oauth", StringComparison.OrdinalIgnoreCase) &&
                 IsYouTubeSourceLocator(_options.YouTube.Channel))
            yield return ("youtube", _options.YouTube.Channel!);

        if (_options.Kick.Enabled && !string.IsNullOrWhiteSpace(_options.Kick.Channel))
            yield return ("kick", _options.Kick.Channel);
    }

    /// <summary>
    /// True when automatic discovery owns the YouTube source lifecycle. A manual per-live URL
    /// override keeps the previous behavior, so the existing manual flow stays available.
    /// </summary>
    private bool AutomaticDiscoveryOwnsYouTubeSource =>
        _liveDiscovery.Enabled && string.IsNullOrWhiteSpace(_liveDiscovery.ManualLiveChatUrl);

    private static string? PreferredSimpleConnectionMode(string target) =>
        string.Equals(target, "twitch", StringComparison.OrdinalIgnoreCase) ? "classic" : null;

    private static bool IsYouTubeSourceLocator(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
        return value.Length == 11 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private static async Task ConfigureYouTubeAutoDiscoveryAsync(
        HttpClient client,
        bool enabled,
        CancellationToken cancellationToken)
    {
        using var document = await SendCommandAsync(client, "updateSettings", new
        {
            settings = new { youtubeAutoAdd = enabled }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SourceInfo>> GetSourcesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var document = await SendCommandAsync(client, "getSources", new { }, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("payload", out var payload)) return [];
        var array = payload.ValueKind == JsonValueKind.Array
            ? payload
            : payload.TryGetProperty("sources", out var sources) ? sources : default;
        if (array.ValueKind != JsonValueKind.Array) return [];
        var result = new List<SourceInfo>();
        foreach (var item in array.EnumerateArray())
        {
            var id = ReadString(item, "id") ?? ReadString(item, "sourceId");
            var target = ReadString(item, "target");
            var username = ReadString(item, "username") ?? ReadString(item, "channel") ?? ReadString(item, "value");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target)) continue;
            var active = ReadBoolean(item, "active") || ReadBoolean(item, "isActive") ||
                         string.Equals(ReadString(item, "state"), "active", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(ReadString(item, "status"), "active", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(ReadString(item, "status"), "running", StringComparison.OrdinalIgnoreCase);
            result.Add(new SourceInfo(
                id,
                target,
                username,
                active,
                ReadString(item, "connectionMode"),
                ReadString(item, "videoId")));
        }
        return result;
    }

    private static async Task<JsonDocument> SendCommandAsync(
        HttpClient client,
        string action,
        object value,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("api/v1/command", new { action, value }, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            document.Dispose();
            throw new InvalidOperationException("SSApp rejected a source command.");
        }
        return document;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private void UpdateSnapshot(
        LiveChatProviderState? state = null,
        string? error = null,
        bool? processRunning = null,
        bool? transportReady = null,
        bool? captureReady = null,
        DateTimeOffset? connectedAt = null,
        DateTimeOffset? lastEventAt = null)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = state ?? _snapshot.State,
                Error = error,
                ProcessRunning = processRunning ?? _snapshot.ProcessRunning,
                TransportReady = transportReady ?? _snapshot.TransportReady,
                CaptureReady = captureReady ?? _snapshot.CaptureReady,
                LastConnectedAtUtc = connectedAt ?? _snapshot.LastConnectedAtUtc,
                LastEventAtUtc = lastEventAt ?? _snapshot.LastEventAtUtc,
                PlatformsObserved = _platformsObserved.Order(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
    }

    private static string ConfiguredPlatforms(SocialStreamNinjaOptions options) => string.Join(
        ',',
        new[]
        {
            options.Twitch.Enabled ? "Twitch" : null,
            options.YouTube.Enabled ? "YouTube" : null,
            options.Kick.Enabled ? "Kick" : null
        }.Where(item => item is not null));

    private sealed record SourceInfo(
        string Id,
        string Target,
        string? Username,
        bool Active,
        string? ConnectionMode,
        string? VideoId);
}
