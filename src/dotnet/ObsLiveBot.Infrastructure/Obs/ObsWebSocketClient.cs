using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Infrastructure.Configuration;

namespace ObsLiveBot.Infrastructure.Obs;

public sealed class ObsWebSocketClient(
    IOptions<ObsWebSocketOptions> options,
    ILogger<ObsWebSocketClient> logger) : IObsProtocolClient, IObsRequestClient, IAsyncDisposable
{
    private readonly ObsWebSocketOptions _options = options.Value;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<ObsExternalEvent> _events = Channel.CreateUnbounded<ObsExternalEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCancellation;
    private TaskCompletionSource _completion = NewCompletionSource();
    private volatile bool _connected;
    private int _disposed;

    public bool IsConnected => _connected;

    public Task Completion => _completion.Task;

    public ChannelReader<ObsExternalEvent> Events => _events.Reader;

    public Task<JsonElement> SendRequestAsync(
        string requestType,
        object requestData,
        CancellationToken cancellationToken) =>
        RequestAsync(requestType, requestData, cancellationToken);

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connected)
            {
                return;
            }

            await DisposeSocketAsync().ConfigureAwait(false);
            var socket = new ClientWebSocket();
            var uri = new Uri($"ws://{_options.Host}:{_options.Port}", UriKind.Absolute);
            var authenticationRequired = false;

            try
            {
                await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                using var hello = await ReceiveDocumentAsync(socket, cancellationToken).ConfigureAwait(false);
                EnsureOpcode(hello.RootElement, 0, "Hello");

                var helloData = hello.RootElement.GetProperty("d");
                authenticationRequired = helloData.TryGetProperty("authentication", out _);
                logger.LogInformation(
                    "OBS_WEBSOCKET_HELLO webSocketVersion={WebSocketVersion} authenticationRequired={AuthenticationRequired}",
                    GetRequiredString(helloData, "obsWebSocketVersion"),
                    authenticationRequired);
                var identify = ObsProtocolMessages.CreateIdentify(helloData, _options.Password);
                await SendAsync(socket, identify, cancellationToken).ConfigureAwait(false);

                using var identified = await ReceiveDocumentAsync(socket, cancellationToken).ConfigureAwait(false);
                EnsureOpcode(identified.RootElement, 2, "Identified");

                _socket = socket;
                _receiveCancellation = new CancellationTokenSource();
                _completion = NewCompletionSource();
                _connected = true;
                _ = Task.Run(() => ReceiveLoopAsync(socket, _receiveCancellation.Token), CancellationToken.None);
            }
            catch (WebSocketException exception) when (exception.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                socket.Dispose();
                if (authenticationRequired)
                {
                    throw new ObsAuthenticationException("OBS WebSocket rejected authentication during identification.");
                }

                throw new IOException("OBS WebSocket closed during identification.", exception);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var socket = _socket;
            if (socket is null)
            {
                _connected = false;
                _completion.TrySetResult();
                return;
            }

            _connected = false;
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Service shutdown",
                        cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    // The remote side may already be gone. Shutdown remains graceful locally.
                }
            }

            _receiveCancellation?.Cancel();
            _completion.TrySetResult();
            await DisposeSocketAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<ObsVersionInfo> GetVersionAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("GetVersion", cancellationToken).ConfigureAwait(false);
        return new ObsVersionInfo(
            GetRequiredString(data, "obsVersion"),
            GetRequiredString(data, "obsWebSocketVersion"));
    }

    public async Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("GetCurrentProgramScene", cancellationToken).ConfigureAwait(false);
        return GetRequiredString(data, "currentProgramSceneName");
    }

    public async Task<bool> GetStreamStatusAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("GetStreamStatus", cancellationToken).ConfigureAwait(false);
        return data.GetProperty("outputActive").GetBoolean();
    }

    public async Task<ObsRecordStatus> GetRecordStatusAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("GetRecordStatus", cancellationToken).ConfigureAwait(false);
        return new ObsRecordStatus(
            data.GetProperty("outputActive").GetBoolean(),
            data.TryGetProperty("outputPaused", out var paused) && paused.GetBoolean());
    }

    public async Task<string> GetCurrentSceneCollectionAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("GetSceneCollectionList", cancellationToken).ConfigureAwait(false);
        return GetRequiredString(data, "currentSceneCollectionName");
    }

    public async Task<string> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        var data = await RequestAsync("GetProfileList", cancellationToken).ConfigureAwait(false);
        return GetRequiredString(data, "currentProfileName");
    }

    public Task<bool?> GetReplayBufferStatusAsync(CancellationToken cancellationToken) =>
        GetOptionalOutputStatusAsync("GetReplayBufferStatus", cancellationToken);

    public Task<bool?> GetVirtualCameraStatusAsync(CancellationToken cancellationToken) =>
        GetOptionalOutputStatusAsync("GetVirtualCamStatus", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _sendGate.Dispose();
        _receiveCancellation?.Dispose();
    }

    private async Task<JsonElement> RequestAsync(string requestType, CancellationToken cancellationToken)
        => await RequestAsync(requestType, new { }, cancellationToken).ConfigureAwait(false);

    private async Task<JsonElement> RequestAsync(
        string requestType,
        object requestData,
        CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (!_connected || socket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("OBS WebSocket is not connected.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Unable to register OBS WebSocket request.");
        }

        try
        {
            var message = new
            {
                op = 6,
                d = new { requestType, requestId, requestData }
            };
            await SendAsync(socket, message, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var document = await ReceiveDocumentAsync(socket, cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                var opcode = root.GetProperty("op").GetInt32();
                if (opcode == 5)
                {
                    var eventMessage = root.GetProperty("d");
                    var eventType = GetRequiredString(eventMessage, "eventType");
                    var eventData = eventMessage.TryGetProperty("eventData", out var dataElement)
                        ? dataElement.Clone()
                        : JsonDocument.Parse("{}").RootElement.Clone();
                    _events.Writer.TryWrite(new ObsExternalEvent(eventType, eventData, DateTimeOffset.UtcNow));
                    continue;
                }

                if (opcode != 7)
                {
                    continue;
                }

                var data = root.GetProperty("d");
                var requestId = GetRequiredString(data, "requestId");
                if (!_pending.TryRemove(requestId, out var completion))
                {
                    continue;
                }

                var requestStatus = data.GetProperty("requestStatus");
                if (!requestStatus.GetProperty("result").GetBoolean())
                {
                    var code = requestStatus.GetProperty("code").GetInt32();
                    completion.TrySetException(new ObsRequestException(GetRequiredString(data, "requestType"), code));
                    continue;
                }

                var responseData = data.TryGetProperty("responseData", out var response)
                    ? response.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone();
                completion.TrySetResult(responseData);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning("OBS_DISCONNECTED transport={TransportError}", exception.GetType().Name);
        }
        finally
        {
            _connected = false;
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new IOException("OBS WebSocket connection closed."));
            }

            _pending.Clear();
            _completion.TrySetResult();
        }
    }

    private async Task<bool?> GetOptionalOutputStatusAsync(string requestType, CancellationToken cancellationToken)
    {
        try
        {
            var data = await RequestAsync(requestType, cancellationToken).ConfigureAwait(false);
            return data.GetProperty("outputActive").GetBoolean();
        }
        catch (ObsRequestException exception) when (exception.StatusCode is 501 or 600 or 601)
        {
            logger.LogInformation("OBS_OPTIONAL_CAPABILITY_UNAVAILABLE requestType={RequestType}", requestType);
            return null;
        }
    }

    private async Task SendAsync(ClientWebSocket socket, object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private static async Task<JsonDocument> ReceiveDocumentAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            }

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
        }
        while (!result.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray());
    }

    private static void EnsureOpcode(JsonElement message, int expectedOpcode, string expectedName)
    {
        var opcode = message.GetProperty("op").GetInt32();
        if (opcode != expectedOpcode)
        {
            throw new InvalidDataException($"Expected OBS WebSocket {expectedName} message.");
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString()
        ?? throw new InvalidDataException($"OBS response property '{propertyName}' is missing.");

    private async Task DisposeSocketAsync()
    {
        _receiveCancellation?.Cancel();
        _receiveCancellation?.Dispose();
        _receiveCancellation = null;
        _socket?.Dispose();
        _socket = null;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
