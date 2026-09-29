using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Chat.Ingest;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Chat;

/// <summary>Local installed-chatbot EventSub WebSocket reader for channel.chat.message v1.</summary>
public sealed class TwitchEventSubLiveChatProvider(
    IOptions<TwitchOAuthOptions> options,
    ITwitchTokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    ISender sender,
    TimeProvider timeProvider,
    ILogger<TwitchEventSubLiveChatProvider> logger) : ILiveChatProvider, ILiveChatProviderReconnectSignal
{
    private const int MaxWebSocketMessageBytes = 256 * 1024;
    private static readonly string[] RequiredScopes = ["user:read:chat"];
    private readonly object _gate = new();
    private readonly Channel<bool> _reconnectSignals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private LiveChatProviderSnapshot _snapshot = new(
        LiveChatProviderType.Twitch,
        options.Value.Enabled,
        options.Value.Enabled ? LiveChatProviderState.NotConfigured : LiveChatProviderState.Disabled,
        options.Value.Channel,
        null, null, null);
    private string? _nextSessionUrl;
    private ClientWebSocket? _activeSocket;

    public LiveChatProviderType Provider => LiveChatProviderType.Twitch;
    public LiveChatProviderSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public TimeSpan? RetryAfter => null;
    public Task Completion => Task.CompletedTask;

    public void SetLifecycleState(LiveChatProviderState state, string? error = null) => UpdateState(state, error);

    public void SignalReconnect()
    {
        _reconnectSignals.Writer.TryWrite(true);
        try { Volatile.Read(ref _activeSocket)?.Abort(); }
        catch (ObjectDisposedException) { }
    }

    public async Task WaitForReconnectSignalAsync(CancellationToken cancellationToken) =>
        _ = await _reconnectSignals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            UpdateState(LiveChatProviderState.Disabled, null);
            return;
        }
        if (string.IsNullOrWhiteSpace(configuration.ClientId) || string.IsNullOrWhiteSpace(configuration.Channel))
        {
            UpdateState(LiveChatProviderState.NotConfigured, "twitch_not_configured");
            return;
        }

        TwitchOAuthTokens? tokens;
        try
        {
            if (!await tokenStore.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
            {
                UpdateState(LiveChatProviderState.Faulted, "credential_helper_unavailable");
                return;
            }
            tokens = await tokenStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("TWITCH_CREDENTIAL_READ_FAILED errorType={ErrorType}", exception.GetType().Name);
            UpdateState(LiveChatProviderState.Faulted, "credential_helper_unavailable");
            return;
        }

        if (tokens is null)
        {
            UpdateState(LiveChatProviderState.AuthenticationRequired, "authorization_required");
            return;
        }
        if (tokens.ExpiresAtUtc <= timeProvider.GetUtcNow().AddMinutes(5))
        {
            tokens = await RefreshTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
            if (tokens is null)
            {
                await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
                UpdateState(LiveChatProviderState.AuthenticationRequired, "authorization_refresh_required");
                return;
            }
        }

        UpdateState(LiveChatProviderState.Connecting, null);
        var api = httpClientFactory.CreateClient("TwitchApi");
        var validated = await ValidateTokenAsync(api, tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        if (validated is null || validated.ClientId != configuration.ClientId ||
            validated.UserId != tokens.UserId ||
            validated.Scopes is null || !RequiredScopes.All(scope => validated.Scopes.Contains(scope, StringComparer.Ordinal)))
        {
            await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
            UpdateState(LiveChatProviderState.AuthenticationRequired, "authorization_invalid");
            return;
        }

        if (!string.Equals(validated.Login, configuration.Channel, StringComparison.OrdinalIgnoreCase))
        {
            UpdateState(LiveChatProviderState.AuthenticationFailed, "broadcaster_account_mismatch");
            return;
        }

        var initialUrl = Interlocked.Exchange(ref _nextSessionUrl, null);
        var reconnecting = !string.IsNullOrWhiteSpace(initialUrl);
        ClientWebSocket? socket = new();
        try
        {
            Interlocked.Exchange(ref _activeSocket, socket);
            socket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
            UpdateState(reconnecting ? LiveChatProviderState.Reconnecting : LiveChatProviderState.Connecting, null);
            await socket.ConnectAsync(
                new Uri(initialUrl ?? "wss://eventsub.wss.twitch.tv/ws"), cancellationToken).ConfigureAwait(false);

            using var welcome = await ReceiveJsonAsync(socket, TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            var (sessionId, keepaliveSeconds) = ReadSessionWelcome(welcome.RootElement);
            if (!reconnecting)
            {
                await CreateSubscriptionAsync(api, tokens, sessionId, validated.UserId!, configuration.Channel, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                logger.LogInformation("TWITCH_EVENTSUB_SESSION_MIGRATED");
            }

            UpdateState(LiveChatProviderState.Connected, null);
            var lastTokenValidation = timeProvider.GetUtcNow();
            while (!cancellationToken.IsCancellationRequested)
            {
                using var message = await ReceiveJsonAsync(
                    socket, TimeSpan.FromSeconds(keepaliveSeconds), cancellationToken).ConfigureAwait(false);
                var now = timeProvider.GetUtcNow();
                if (now - lastTokenValidation >= TimeSpan.FromHours(1))
                {
                    var hourlyValidation = await ValidateTokenAsync(api, tokens.AccessToken, cancellationToken)
                        .ConfigureAwait(false);
                    if (hourlyValidation is null || hourlyValidation.ClientId != configuration.ClientId ||
                        hourlyValidation.UserId != tokens.UserId || hourlyValidation.Scopes is null ||
                        !RequiredScopes.All(scope => hourlyValidation.Scopes.Contains(scope, StringComparer.Ordinal)))
                    {
                        await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
                        UpdateState(LiveChatProviderState.AuthenticationRequired, "authorization_revoked");
                        return;
                    }
                    lastTokenValidation = now;
                }
                if (tokens.ExpiresAtUtc <= now.AddMinutes(30))
                {
                    var refreshed = await RefreshTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
                    if (refreshed is null)
                    {
                        await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
                        UpdateState(LiveChatProviderState.AuthenticationRequired, "authorization_refresh_required");
                        return;
                    }
                    tokens = refreshed;
                    lastTokenValidation = now;
                }
                var messageType = GetMessageType(message.RootElement);
                switch (messageType)
                {
                    case "session_keepalive":
                        continue;
                    case "session_reconnect":
                    {
                        var reconnectUrl = message.RootElement.GetProperty("payload").GetProperty("session")
                            .GetProperty("reconnect_url").GetString();
                        if (!Uri.TryCreate(reconnectUrl, UriKind.Absolute, out var reconnectUri) ||
                            reconnectUri.Scheme != "wss")
                            throw new InvalidDataException("Twitch EventSub reconnect URL is invalid.");

                        UpdateState(LiveChatProviderState.Reconnecting, null);
                        using var handoffCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        var replacementTask = OpenReplacementSocketAsync(reconnectUri, handoffCancellation.Token);
                        try
                        {
                            while (!replacementTask.IsCompleted)
                            {
                                using var oldReadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                var oldReadTask = ReceiveJsonAsync(
                                    socket, TimeSpan.FromSeconds(keepaliveSeconds), oldReadCancellation.Token);
                                var completed = await Task.WhenAny(replacementTask, oldReadTask).ConfigureAwait(false);
                                if (completed == replacementTask)
                                {
                                    oldReadCancellation.Cancel();
                                    try
                                    {
                                        using var lastOldMessage = await oldReadTask.ConfigureAwait(false);
                                        if (!await ProcessHandoffMessageAsync(lastOldMessage.RootElement, cancellationToken)
                                                .ConfigureAwait(false))
                                        {
                                            await AbandonReplacementAsync(replacementTask, handoffCancellation, socket)
                                                .ConfigureAwait(false);
                                            return;
                                        }
                                    }
                                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                                    {
                                        // The pending old-session read is abandoned only after replacement welcome.
                                    }
                                    break;
                                }

                                try
                                {
                                    using var oldMessage = await oldReadTask.ConfigureAwait(false);
                                    if (!await ProcessHandoffMessageAsync(oldMessage.RootElement, cancellationToken)
                                            .ConfigureAwait(false))
                                    {
                                        await AbandonReplacementAsync(replacementTask, handoffCancellation, socket)
                                            .ConfigureAwait(false);
                                        return;
                                    }
                                }
                                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                                {
                                    // Old session silence is tolerated while the bounded replacement welcome is pending.
                                    break;
                                }
                            }

                            var (replacement, replacementKeepalive) = await replacementTask.ConfigureAwait(false);

                            // Twitch transfers subscriptions during session_reconnect. Do not POST a duplicate.
                            var previous = socket;
                            socket = replacement;
                            keepaliveSeconds = replacementKeepalive;
                            Interlocked.Exchange(ref _activeSocket, socket);
                            previous.Dispose();
                            UpdateState(LiveChatProviderState.Connected, null);
                            logger.LogInformation("TWITCH_EVENTSUB_SESSION_MIGRATED");
                        }
                        catch (Exception exception)
                        {
                            // Ensure a candidate still connecting/waiting for welcome cannot outlive this handoff.
                            await AbandonReplacementAsync(replacementTask, handoffCancellation, socket)
                                .ConfigureAwait(false);

                            UpdateState(LiveChatProviderState.Reconnecting, "session_handoff_failed");
                            logger.LogWarning("TWITCH_EVENTSUB_HANDOFF_FAILED errorType={ErrorType}", exception.GetType().Name);
                            if (cancellationToken.IsCancellationRequested)
                                throw;
                        }
                        continue;
                    }
                    case "revocation":
                        await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
                        UpdateState(LiveChatProviderState.AuthenticationRequired, "eventsub_authorization_revoked");
                        return;
                    case "notification":
                        await ProcessNotificationAsync(message.RootElement, cancellationToken).ConfigureAwait(false);
                        break;
                    case "session_welcome":
                        throw new InvalidDataException("Unexpected Twitch EventSub welcome message.");
                }
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _activeSocket, null, socket);
            socket?.Dispose();
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateState(options.Value.Enabled ? LiveChatProviderState.Disconnected : LiveChatProviderState.Disabled, null);
        return Task.CompletedTask;
    }

    private async Task ProcessNotificationAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var chatEvent = MapChatMessage(root, timeProvider.GetUtcNow());
        if (chatEvent is null)
            return;

        var result = await sender.Send(new IngestLiveChatEventCommand(chatEvent), cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value.Accepted)
        {
            lock (_gate) _snapshot = _snapshot with { LastEventAtUtc = result.Value.Event?.ReceivedAtUtc };
            logger.LogInformation(
                "TWITCH_CHAT_MESSAGE_ACCEPTED eventId={EventId} userId={UserId} correlationId={CorrelationId}",
                result.Value.Event?.EventId,
                chatEvent.User.UserId,
                result.Value.Event?.CorrelationId);
        }
    }

    private async Task<bool> ProcessHandoffMessageAsync(JsonElement root, CancellationToken cancellationToken)
    {
        switch (GetMessageType(root))
        {
            case "notification":
                await ProcessNotificationAsync(root, cancellationToken).ConfigureAwait(false);
                return true;
            case "session_keepalive":
                return true;
            case "revocation":
                await tokenStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
                UpdateState(LiveChatProviderState.AuthenticationRequired, "eventsub_authorization_revoked");
                return false;
            default:
                return true;
        }
    }

    private static async Task AbandonReplacementAsync(
        Task<(ClientWebSocket Socket, int KeepaliveSeconds)> replacementTask,
        CancellationTokenSource handoffCancellation,
        ClientWebSocket currentSocket)
    {
        handoffCancellation.Cancel();
        try
        {
            var abandoned = await replacementTask.ConfigureAwait(false);
            if (!ReferenceEquals(abandoned.Socket, currentSocket))
                abandoned.Socket.Dispose();
        }
        catch (Exception) when (handoffCancellation.IsCancellationRequested)
        {
            // OpenReplacementSocketAsync disposes unsuccessful candidates.
        }
    }

    private static async Task<(ClientWebSocket Socket, int KeepaliveSeconds)> OpenReplacementSocketAsync(
        Uri reconnectUri, CancellationToken cancellationToken)
    {
        var replacement = new ClientWebSocket();
        replacement.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
        try
        {
            await replacement.ConnectAsync(reconnectUri, cancellationToken).ConfigureAwait(false);
            using var welcome = await ReceiveJsonAsync(
                replacement, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            var (_, keepaliveSeconds) = ReadSessionWelcome(welcome.RootElement);
            return (replacement, keepaliveSeconds);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    public static ProviderLiveChatEvent? MapChatMessage(JsonElement root, DateTimeOffset receivedAtUtc)
    {
        var metadata = root.GetProperty("metadata");
        if (metadata.GetProperty("message_type").GetString() != "notification" ||
            metadata.GetProperty("subscription_type").GetString() != "channel.chat.message")
            return null;

        var payload = root.GetProperty("payload").GetProperty("event");
        var message = payload.GetProperty("message");
        var text = message.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
        if (string.IsNullOrEmpty(text) && message.TryGetProperty("fragments", out var fragments))
            text = string.Concat(fragments.EnumerateArray().Select(fragment =>
                fragment.TryGetProperty("text", out var fragmentText) ? fragmentText.GetString() : null));

        var broadcasterId = RequiredString(payload, "broadcaster_user_id");
        var broadcasterLogin = RequiredString(payload, "broadcaster_user_login");
        var chatterId = RequiredString(payload, "chatter_user_id");
        var chatterLogin = RequiredString(payload, "chatter_user_login");
        var displayName = RequiredString(payload, "chatter_user_name");
        var timestamp = DateTimeOffset.TryParse(metadata.GetProperty("message_timestamp").GetString(), out var parsed)
            ? parsed
            : receivedAtUtc;
        var badges = payload.TryGetProperty("badges", out var badgeArray)
            ? badgeArray.EnumerateArray().Take(32)
                .Select(badge => badge.TryGetProperty("set_id", out var set) ? set.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
            : [];
        var roles = badges.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ProviderLiveChatEvent(
            LiveChatProviderType.Twitch,
            LiveChatEventType.Message,
            RequiredString(payload, "message_id"),
            broadcasterId,
            broadcasterLogin,
            new LiveChatUser(
                LiveChatProviderType.Twitch,
                chatterId,
                chatterLogin,
                displayName,
                chatterId == broadcasterId,
                roles.Contains("moderator") || roles.Contains("broadcaster"),
                roles.Contains("subscriber"),
                false,
                false,
                badges),
            text,
            timestamp,
            RequiredString(metadata, "message_id"),
            new Dictionary<string, string?>
            {
                ["message_type"] = "channel.chat.message",
                ["color"] = payload.TryGetProperty("color", out var color) ? color.GetString() : null
            });
    }

    private async Task CreateSubscriptionAsync(
        HttpClient client,
        TwitchOAuthTokens tokens,
        string sessionId,
        string userId,
        string broadcasterLogin,
        CancellationToken cancellationToken)
    {
        var broadcasterId = await ResolveBroadcasterIdAsync(client, tokens, broadcasterLogin, cancellationToken)
            .ConfigureAwait(false);
        if (broadcasterId != userId)
            throw new InvalidOperationException("Installed chatbot token must belong to configured broadcaster.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "helix/eventsub/subscriptions")
        {
            Content = JsonContent.Create(new
            {
                type = "channel.chat.message",
                version = "1",
                condition = new { broadcaster_user_id = broadcasterId, user_id = userId },
                transport = new { method = "websocket", session_id = sessionId }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        request.Headers.TryAddWithoutValidation("Client-Id", options.Value.ClientId);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Twitch EventSub subscription was rejected.", null, response.StatusCode);
    }

    private async Task<string> ResolveBroadcasterIdAsync(
        HttpClient client, TwitchOAuthTokens tokens, string login, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"helix/users?login={Uri.EscapeDataString(login)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        request.Headers.TryAddWithoutValidation("Client-Id", options.Value.ClientId);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var users = body.RootElement.GetProperty("data");
        if (users.GetArrayLength() != 1) throw new InvalidDataException("Configured Twitch broadcaster was not found.");
        var user = users[0];
        return RequiredString(user, "id");
    }

    private async Task<TokenValidation?> ValidateTokenAsync(
        HttpClient client, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "oauth2/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TokenValidation>(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TwitchOAuthTokens?> RefreshTokensAsync(
        TwitchOAuthTokens previous, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("TwitchOAuth");
            using var response = await client.PostAsync("oauth2/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = options.Value.ClientId!,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = previous.RefreshToken
                }), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var refreshed = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken) ||
                string.IsNullOrWhiteSpace(refreshed.RefreshToken) || refreshed.ExpiresIn <= 0)
                return null;

            var validation = await ValidateTokenAsync(client, refreshed.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            if (validation is null || validation.ClientId != options.Value.ClientId ||
                validation.UserId != previous.UserId || validation.Scopes is null ||
                !RequiredScopes.All(scope => validation.Scopes.Contains(scope, StringComparer.Ordinal)))
                return null;

            var now = timeProvider.GetUtcNow();
            var updated = previous with
            {
                AccessToken = refreshed.AccessToken,
                RefreshToken = refreshed.RefreshToken,
                StoredAtUtc = now,
                ExpiresAtUtc = now.AddSeconds(Math.Min(refreshed.ExpiresIn, validation.ExpiresIn)),
                Scopes = validation.Scopes
            };
            await tokenStore.WriteAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("TWITCH_TOKEN_REFRESH_FAILED errorType={ErrorType}", exception.GetType().Name);
            return null;
        }
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(
        ClientWebSocket socket, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var buffer = new byte[8 * 1024];
        using var output = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, timeoutSource.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Twitch EventSub WebSocket closed.");
            if (result.MessageType != WebSocketMessageType.Text || output.Length + result.Count > MaxWebSocketMessageBytes)
                throw new InvalidDataException("Twitch EventSub frame is invalid or oversized.");
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(output.ToArray());
    }

    private static string GetMessageType(JsonElement root) =>
        root.GetProperty("metadata").GetProperty("message_type").GetString() ?? string.Empty;

    public static (string SessionId, int KeepaliveSeconds) ReadSessionWelcome(JsonElement root)
    {
        if (GetMessageType(root) != "session_welcome")
            throw new InvalidDataException("Twitch EventSub did not begin with session_welcome.");
        var session = root.GetProperty("payload").GetProperty("session");
        var sessionId = RequiredString(session, "id");
        var keepaliveSeconds = session.GetProperty("keepalive_timeout_seconds").GetInt32();
        if (keepaliveSeconds is < 1 or > 600)
            throw new InvalidDataException("Twitch EventSub keepalive timeout is invalid.");
        return (sessionId, keepaliveSeconds);
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new InvalidDataException($"Twitch EventSub field '{name}' is missing.");

    private void UpdateState(LiveChatProviderState state, string? error)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                State = state,
                Error = error,
                LastConnectedAtUtc = state == LiveChatProviderState.Connected
                    ? timeProvider.GetUtcNow()
                    : _snapshot.LastConnectedAtUtc
            };
        }
        logger.LogInformation("LIVE_CHAT_PROVIDER_STATE provider=Twitch state={ProviderState} error={Error}", state, error);
    }

    private sealed record TokenValidation(
        [property: JsonPropertyName("client_id")] string? ClientId,
        [property: JsonPropertyName("login")] string? Login,
        [property: JsonPropertyName("user_id")] string? UserId,
        [property: JsonPropertyName("scopes")] string[]? Scopes,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record RefreshTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
