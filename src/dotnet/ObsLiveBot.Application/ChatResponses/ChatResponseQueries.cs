using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.ChatResponses;

public sealed record GetChatResponseStateQuery : IRequest<ChatResponseStateSnapshot>;

public sealed record GetRecentChatResponsesQuery(int Limit) : IRequest<IReadOnlyList<ChatResponseResult>>;

public sealed record GetChatResponseSendersQuery : IRequest<IReadOnlyList<IChatResponseSender>>;

/// <summary>
/// Refuses a nonsensical limit instead of silently clamping it. A caller that asked for zero records, or
/// for a million, made a mistake worth reporting; quietly returning a different number would hide it behind
/// a plausible-looking response.
/// </summary>
public sealed class GetRecentChatResponsesQueryValidator : AbstractValidator<GetRecentChatResponsesQuery>
{
    public GetRecentChatResponsesQueryValidator() =>
        RuleFor(query => query.Limit).InclusiveBetween(1, 100);
}

public sealed class GetChatResponseStateQueryHandler(IChatResponseWriter writer)
    : IRequestHandler<GetChatResponseStateQuery, ChatResponseStateSnapshot>
{
    public Task<ChatResponseStateSnapshot> Handle(
        GetChatResponseStateQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(writer.GetState());
}

public sealed class GetRecentChatResponsesQueryHandler(IChatResponseWriter writer)
    : IRequestHandler<GetRecentChatResponsesQuery, IReadOnlyList<ChatResponseResult>>
{
    public Task<IReadOnlyList<ChatResponseResult>> Handle(
        GetRecentChatResponsesQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(writer.GetRecent(request.Limit));
}

public sealed class GetChatResponseSendersQueryHandler(IChatResponseSenderRegistry registry)
    : IRequestHandler<GetChatResponseSendersQuery, IReadOnlyList<IChatResponseSender>>
{
    public Task<IReadOnlyList<IChatResponseSender>> Handle(
        GetChatResponseSendersQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult(registry.GetSenders());
}
