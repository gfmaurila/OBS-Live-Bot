using Ardalis.Result;
using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Chat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Features.Chat;

public sealed record GetLiveChatProvidersQuery : IRequest<Result<LiveChatProvidersResponse>>;

public sealed class GetLiveChatProvidersQueryHandler(ILiveChatProviderRegistry registry)
    : IRequestHandler<GetLiveChatProvidersQuery, Result<LiveChatProvidersResponse>>
{
    public Task<Result<LiveChatProvidersResponse>> Handle(
        GetLiveChatProvidersQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providers = registry.GetProviders().Select(ChatResponseMapper.Map).ToArray();
        return Task.FromResult(Result.Success(new LiveChatProvidersResponse(providers)));
    }
}

public sealed record GetLiveChatStateQuery : IRequest<Result<LiveChatStateResponse>>;

public sealed class GetLiveChatStateQueryHandler(
    ILiveChatBuffer buffer,
    ILiveChatProviderRegistry registry)
    : IRequestHandler<GetLiveChatStateQuery, Result<LiveChatStateResponse>>
{
    public Task<Result<LiveChatStateResponse>> Handle(
        GetLiveChatStateQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = buffer.GetState(registry.GetProviders());
        return Task.FromResult(Result.Success(new LiveChatStateResponse(
            state.Status,
            state.ConnectedProviders,
            state.EnabledProviders,
            state.EventsReceived,
            state.MessagesReceived,
            state.BufferSize,
            state.BufferCapacity,
            state.LastMessageAtUtc)));
    }
}

public sealed record GetLiveChatMessagesQuery(int Limit = 20, string? Provider = null)
    : IRequest<Result<IReadOnlyList<LiveChatEventResponse>>>;

public sealed record GetLiveChatEventsQuery(int Limit = 20, string? Provider = null)
    : IRequest<Result<IReadOnlyList<LiveChatEventResponse>>>;

public sealed class GetLiveChatMessagesQueryValidator : AbstractValidator<GetLiveChatMessagesQuery>
{
    public GetLiveChatMessagesQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(1, 100);
        RuleFor(query => query.Provider).Must(ChatQueryValidation.IsKnownProvider)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Provider must be Twitch, YouTube, Kick, TikTok, or SocialStreamNinja.");
    }
}

public sealed class GetLiveChatEventsQueryValidator : AbstractValidator<GetLiveChatEventsQuery>
{
    public GetLiveChatEventsQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(1, 100);
        RuleFor(query => query.Provider).Must(ChatQueryValidation.IsKnownProvider)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Provider must be Twitch, YouTube, Kick, TikTok, or SocialStreamNinja.");
    }
}

public sealed class GetLiveChatMessagesQueryHandler(ILiveChatBuffer buffer)
    : IRequestHandler<GetLiveChatMessagesQuery, Result<IReadOnlyList<LiveChatEventResponse>>>
{
    public Task<Result<IReadOnlyList<LiveChatEventResponse>>> Handle(
        GetLiveChatMessagesQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = ChatQueryValidation.ParseProvider(request.Provider);
        IReadOnlyList<LiveChatEventResponse> response = buffer
            .GetRecent(request.Limit, provider, LiveChatEventType.Message)
            .Select(ChatResponseMapper.Map)
            .ToArray();
        return Task.FromResult(Result.Success(response));
    }
}

public sealed class GetLiveChatEventsQueryHandler(ILiveChatBuffer buffer)
    : IRequestHandler<GetLiveChatEventsQuery, Result<IReadOnlyList<LiveChatEventResponse>>>
{
    public Task<Result<IReadOnlyList<LiveChatEventResponse>>> Handle(
        GetLiveChatEventsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = ChatQueryValidation.ParseProvider(request.Provider);
        IReadOnlyList<LiveChatEventResponse> response = buffer
            .GetRecent(request.Limit, provider)
            .Select(ChatResponseMapper.Map)
            .ToArray();
        return Task.FromResult(Result.Success(response));
    }
}

internal static class ChatQueryValidation
{
    public static bool IsKnownProvider(string? value) =>
        Enum.TryParse<LiveChatProviderType>(value, true, out var provider) &&
        provider is LiveChatProviderType.Twitch or LiveChatProviderType.YouTube or LiveChatProviderType.Kick or
            LiveChatProviderType.TikTok or LiveChatProviderType.SocialStreamNinja;

    public static LiveChatProviderType? ParseProvider(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.Parse<LiveChatProviderType>(value, true);
}

internal static class ChatResponseMapper
{
    public static LiveChatProviderResponse Map(LiveChatProviderSnapshot provider) => new(
        provider.Provider.ToString(),
        provider.Provider == LiveChatProviderType.SocialStreamNinja ? "SimpleCapture" : "OfficialApi",
        provider.Enabled,
        provider.State.ToString(),
        provider.IsConnected,
        provider.Channel,
        provider.LastConnectedAtUtc,
        provider.LastEventAtUtc,
        provider.Error,
        provider.ProcessRunning,
        provider.TransportReady,
        provider.CaptureReady,
        provider.PlatformsObserved ?? []);

    public static LiveChatEventResponse Map(LiveChatEvent chatEvent) => new(
        chatEvent.EventId,
        chatEvent.EventType.ToString(),
        chatEvent.Provider.ToString(),
        chatEvent.ProviderEventId,
        chatEvent.ChannelId,
        chatEvent.ChannelName,
        new LiveChatUserResponse(
            chatEvent.User.Provider.ToString(),
            chatEvent.User.UserId,
            chatEvent.User.Identity,
            chatEvent.User.Username,
            chatEvent.User.DisplayName,
            chatEvent.User.IsBroadcaster,
            chatEvent.User.IsModerator,
            chatEvent.User.IsSubscriber,
            chatEvent.User.IsVerified,
            chatEvent.User.IsBot,
            chatEvent.User.Badges),
        chatEvent.Message,
        chatEvent.TimestampUtc,
        chatEvent.ReceivedAtUtc,
        chatEvent.Sequence,
        chatEvent.CorrelationId,
        chatEvent.Metadata);
}
