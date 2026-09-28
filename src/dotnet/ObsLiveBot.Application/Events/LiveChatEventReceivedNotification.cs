using MediatR;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Events;

public sealed record LiveChatEventReceivedNotification(LiveChatEvent ChatEvent) : INotification;
