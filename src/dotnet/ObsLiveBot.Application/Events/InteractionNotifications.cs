using MediatR;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Events;

public sealed record InteractionDecidedNotification(InteractionDecision Decision) : INotification;
public sealed record InteractionCompletedNotification(InteractionResult Result) : INotification;
public sealed record InteractionFailedNotification(InteractionResult Result) : INotification;
