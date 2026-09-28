using MediatR;
using ObsLiveBot.Domain.Obs;

namespace ObsLiveBot.Application.Events;

public sealed record ObsStateChangedNotification(ObsEventEnvelope Envelope) : INotification;
