using Ardalis.Result;
using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Obs;

namespace ObsLiveBot.Application.Features.Obs.GetEvents;

public sealed record GetRecentObsEventsQuery(int Limit = 20) : IRequest<Result<IReadOnlyList<ObsEventResponse>>>;

public sealed class GetRecentObsEventsQueryValidator : AbstractValidator<GetRecentObsEventsQuery>
{
    public GetRecentObsEventsQueryValidator() => RuleFor(query => query.Limit).InclusiveBetween(1, 100);
}

public sealed class GetRecentObsEventsQueryHandler(IObsLiveStateReader reader)
    : IRequestHandler<GetRecentObsEventsQuery, Result<IReadOnlyList<ObsEventResponse>>>
{
    public Task<Result<IReadOnlyList<ObsEventResponse>>> Handle(GetRecentObsEventsQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ObsEventResponse> response = reader.GetRecentEvents(request.Limit)
            .Select(item => new ObsEventResponse(
                item.EventId, item.EventType, item.TimestampUtc, item.Source,
                item.CorrelationId, item.ConnectionId, item.Sequence, item.Payload))
            .ToArray();
        return Task.FromResult(Result.Success(response));
    }
}
