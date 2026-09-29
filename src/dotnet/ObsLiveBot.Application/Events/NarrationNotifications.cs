using MediatR;
using ObsLiveBot.Domain.Narration;

namespace ObsLiveBot.Application.Events;

public sealed record NarrationQueuedNotification(NarrationResult Result) : INotification;
public sealed record NarrationStartedNotification(NarrationResult Result) : INotification;
public sealed record NarrationCompletedNotification(NarrationResult Result) : INotification;
public sealed record NarrationFailedNotification(NarrationResult Result) : INotification;
public sealed record NarrationCancelledNotification(NarrationResult Result) : INotification;
