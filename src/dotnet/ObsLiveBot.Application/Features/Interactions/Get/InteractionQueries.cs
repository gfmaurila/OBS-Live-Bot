using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Interactions;

namespace ObsLiveBot.Application.Features.Interactions.Get;

public sealed record GetInteractionStateQuery : IRequest<Result<InteractionStateResponse>>;
public sealed record GetRecentInteractionsQuery(int Limit = 20)
    : IRequest<Result<IReadOnlyList<InteractionResponse>>>;
public sealed record GetInteractionProvidersQuery : IRequest<Result<InteractionProvidersResponse>>;

public sealed class GetRecentInteractionsQueryValidator : AbstractValidator<GetRecentInteractionsQuery>
{
    public GetRecentInteractionsQueryValidator() =>
        RuleFor(query => query.Limit).InclusiveBetween(1, 100);
}

public sealed class GetInteractionStateQueryHandler(
    IInteractionBuffer buffer,
    IInteractionCooldownTracker cooldown,
    IInteractionProviderRegistry providers,
    IOptions<InteractionOptions> options)
    : IRequestHandler<GetInteractionStateQuery, Result<InteractionStateResponse>>
{
    public async Task<Result<InteractionStateResponse>> Handle(
        GetInteractionStateQuery request,
        CancellationToken cancellationToken)
    {
        var ai = providers.GetAiProvider();
        var tts = providers.GetTtsProvider();
        var aiAvailable = ai is not null &&
            await ai.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        var aiState = ai?.GetRuntimeState();
        var ttsAvailable = tts is not null &&
            await tts.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        var ttsState = tts?.GetRuntimeState();
        var state = buffer.GetState(
            options.Value.Enabled,
            aiAvailable,
            ttsAvailable,
            ai?.Name ?? options.Value.AiProvider,
            aiState?.Status ?? "Unavailable",
            aiState?.Model,
            tts?.Name ?? options.Value.TtsProvider,
            ttsState?.Status ?? "Unavailable",
            ttsState?.Voice,
            ttsState?.AudioFormat,
            cooldown.Count,
            cooldown.Capacity);
        return Result.Success(new InteractionStateResponse(
            state.Status,
            state.Total,
            state.Ignored,
            state.Completed,
            state.Failed,
            state.BufferSize,
            state.BufferCapacity,
            state.CooldownEntries,
            state.CooldownCapacity,
            state.LastInteractionAtUtc,
            state.AiProvider,
            state.AiStatus,
            state.AiModel,
            state.TtsProvider,
            state.TtsStatus,
            state.TtsVoice,
            state.TtsAudioFormat));
    }
}

public sealed class GetRecentInteractionsQueryHandler(IInteractionBuffer buffer)
    : IRequestHandler<GetRecentInteractionsQuery, Result<IReadOnlyList<InteractionResponse>>>
{
    public Task<Result<IReadOnlyList<InteractionResponse>>> Handle(
        GetRecentInteractionsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InteractionResponse> response = buffer.GetRecent(request.Limit)
            .Select(InteractionResponseMapper.Map)
            .ToArray();
        return Task.FromResult(Result.Success(response));
    }
}

public sealed class GetInteractionProvidersQueryHandler(IInteractionProviderRegistry providers)
    : IRequestHandler<GetInteractionProvidersQuery, Result<InteractionProvidersResponse>>
{
    public async Task<Result<InteractionProvidersResponse>> Handle(
        GetInteractionProvidersQuery request,
        CancellationToken cancellationToken)
    {
        var snapshots = await providers.GetProvidersAsync(cancellationToken).ConfigureAwait(false);
        var response = snapshots
            .Select(provider => new InteractionProviderResponse(
                provider.Kind,
                provider.Name,
                provider.Selected,
                provider.Available,
                provider.Development,
                provider.Status,
                provider.Model,
                provider.Requests,
                provider.Successes,
                provider.Failures,
                provider.Timeouts,
                provider.BusyRejections,
                provider.AverageDurationMilliseconds,
                provider.LastSuccessAtUtc,
                provider.LastFailureAtUtc,
                provider.Voice,
                provider.AudioFormat))
            .ToArray();
        return Result.Success(new InteractionProvidersResponse(response));
    }
}
