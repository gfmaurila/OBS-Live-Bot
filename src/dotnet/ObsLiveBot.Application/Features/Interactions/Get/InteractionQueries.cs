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
    public Task<Result<InteractionStateResponse>> Handle(
        GetInteractionStateQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ai = providers.GetAiProvider();
        var tts = providers.GetTtsProvider();
        var available = ai?.IsAvailable == true && tts?.IsAvailable == true;
        var state = buffer.GetState(options.Value.Enabled, available, cooldown.Count, cooldown.Capacity);
        return Task.FromResult(Result.Success(new InteractionStateResponse(
            state.Status,
            state.Total,
            state.Ignored,
            state.Completed,
            state.Failed,
            state.BufferSize,
            state.BufferCapacity,
            state.CooldownEntries,
            state.CooldownCapacity,
            state.LastInteractionAtUtc)));
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
    public Task<Result<InteractionProvidersResponse>> Handle(
        GetInteractionProvidersQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = providers.GetProviders()
            .Select(provider => new InteractionProviderResponse(
                provider.Kind,
                provider.Name,
                provider.Selected,
                provider.Available,
                provider.Development,
                provider.Status))
            .ToArray();
        return Task.FromResult(Result.Success(new InteractionProvidersResponse(response)));
    }
}
