using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Infrastructure.Interactions;

public sealed class OllamaAiInteractionProvider : IAiInteractionProvider
{
    private readonly HttpClient _httpClient;
    private readonly OllamaInteractionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OllamaAiInteractionProvider> _logger;
    private readonly SemaphoreSlim _concurrency;
    private int _pendingRequests;
    private int _available;
    private long _requests;
    private long _successes;
    private long _failures;
    private long _timeouts;
    private long _busyRejections;
    private long _totalDurationTicks;
    private DateTimeOffset? _lastSuccessAtUtc;
    private DateTimeOffset? _lastFailureAtUtc;

    public OllamaAiInteractionProvider(
        HttpClient httpClient,
        IOptions<InteractionOptions> options,
        TimeProvider timeProvider,
        ILogger<OllamaAiInteractionProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Ollama;
        _timeProvider = timeProvider;
        _logger = logger;
        _concurrency = new SemaphoreSlim(
            _options.MaxConcurrentRequests,
            _options.MaxConcurrentRequests);
    }

    public string Name => "Ollama";
    public string ModelName => _options.Model;
    public bool IsAvailable => Volatile.Read(ref _available) == 1;
    public bool IsDevelopment => false;

    public AiProviderRuntimeSnapshot GetRuntimeState()
    {
        var successes = Interlocked.Read(ref _successes);
        double? average = successes == 0
            ? null
            : TimeSpan.FromTicks(Interlocked.Read(ref _totalDurationTicks) / successes).TotalMilliseconds;
        return new AiProviderRuntimeSnapshot(
            IsAvailable,
            IsAvailable ? "Ready" : "Unavailable",
            ModelName,
            Interlocked.Read(ref _requests),
            successes,
            Interlocked.Read(ref _failures),
            Interlocked.Read(ref _timeouts),
            Interlocked.Read(ref _busyRejections),
            average,
            _lastSuccessAtUtc,
            _lastFailureAtUtc);
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(_options.TimeoutSeconds, 5)));
        try
        {
            using var response = await _httpClient.GetAsync("api/tags", timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                SetAvailability(false);
                return false;
            }

            var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(
                cancellationToken: timeout.Token).ConfigureAwait(false);
            var available = tags?.Models?.Any(model =>
                string.Equals(model.Name, ModelName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.Model, ModelName, StringComparison.OrdinalIgnoreCase)) == true;
            SetAvailability(available);
            return available;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetAvailability(false);
            return false;
        }
        catch (HttpRequestException)
        {
            SetAvailability(false);
            return false;
        }
        catch (JsonException)
        {
            SetAvailability(false);
            return false;
        }
    }

    public async Task<AiInteractionResponse> GenerateAsync(
        AiInteractionRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        var entered = await _concurrency.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            if (Interlocked.Increment(ref _pendingRequests) > _options.MaxQueuedRequests)
            {
                Interlocked.Decrement(ref _pendingRequests);
                return Failure(request, "OLLAMA_BUSY", busy: true);
            }

            try
            {
                entered = await _concurrency.WaitAsync(
                    TimeSpan.FromSeconds(_options.QueueWaitTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingRequests);
            }
        }

        if (!entered)
        {
            return Failure(request, "OLLAMA_BUSY", busy: true);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var messages = new List<OllamaChatMessage>
            {
                new("system", request.SystemInstructions)
            };
            messages.AddRange(request.ConversationContext.Select(item => new OllamaChatMessage("assistant", item)));
            messages.Add(new OllamaChatMessage("user", request.UserMessage));
            var payload = new OllamaChatRequest(
                ModelName,
                false,
                false,
                messages,
                new OllamaRequestOptions(_options.Temperature, _options.MaxOutputTokens));

            using var response = await _httpClient.PostAsJsonAsync(
                "api/chat", payload, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                SetAvailability(false);
                return Failure(
                    request,
                    $"OLLAMA_HTTP_{(int)response.StatusCode}",
                    Stopwatch.GetElapsedTime(started));
            }

            OllamaChatResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                    cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Failure(request, "OLLAMA_INVALID_RESPONSE", Stopwatch.GetElapsedTime(started));
            }

            if (string.IsNullOrWhiteSpace(body?.Message?.Content))
            {
                return Failure(request, "OLLAMA_EMPTY_RESPONSE", Stopwatch.GetElapsedTime(started));
            }

            var duration = Stopwatch.GetElapsedTime(started);
            SetAvailability(true);
            Interlocked.Increment(ref _successes);
            Interlocked.Add(ref _totalDurationTicks, duration.Ticks);
            _lastSuccessAtUtc = _timeProvider.GetUtcNow();
            return new AiInteractionResponse(
                body.Message.Content,
                Name,
                string.IsNullOrWhiteSpace(body.Model) ? ModelName : body.Model,
                duration,
                true,
                null,
                request.CorrelationId,
                false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetAvailability(false);
            Interlocked.Increment(ref _timeouts);
            return Failure(request, "OLLAMA_TIMEOUT", Stopwatch.GetElapsedTime(started), countFailure: true);
        }
        catch (HttpRequestException exception)
        {
            SetAvailability(false);
            _logger.LogWarning(
                "OLLAMA_REQUEST_FAILED model={Model} errorType={ErrorType}",
                ModelName,
                exception.GetType().Name);
            return Failure(request, "OLLAMA_UNAVAILABLE", Stopwatch.GetElapsedTime(started));
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private AiInteractionResponse Failure(
        AiInteractionRequest request,
        string errorCode,
        TimeSpan? duration = null,
        bool busy = false,
        bool countFailure = true)
    {
        if (countFailure) Interlocked.Increment(ref _failures);
        if (busy) Interlocked.Increment(ref _busyRejections);
        _lastFailureAtUtc = _timeProvider.GetUtcNow();
        return new AiInteractionResponse(
            null,
            Name,
            ModelName,
            duration ?? TimeSpan.Zero,
            false,
            errorCode,
            request.CorrelationId,
            false);
    }

    private void SetAvailability(bool value) => Volatile.Write(ref _available, value ? 1 : 0);

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
        [property: JsonPropertyName("options")] OllamaRequestOptions Options);

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaRequestOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("num_predict")] int MaxOutputTokens);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")] OllamaChatMessage? Message);

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<OllamaModel>? Models);

    private sealed record OllamaModel(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("model")] string? Model);
}
