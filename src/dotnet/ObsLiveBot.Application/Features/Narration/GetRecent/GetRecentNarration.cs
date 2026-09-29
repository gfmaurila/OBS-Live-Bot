using Ardalis.Result;
using FluentValidation;
using MediatR;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;

namespace ObsLiveBot.Application.Features.Narration.GetRecent;

public sealed record GetRecentNarrationQuery(int Limit = 20) : IRequest<Result<NarrationRecentResponse>>;

public sealed class GetRecentNarrationQueryValidator : AbstractValidator<GetRecentNarrationQuery>
{
    public GetRecentNarrationQueryValidator() => RuleFor(query => query.Limit).InclusiveBetween(1, 100);
}

public sealed class GetRecentNarrationQueryHandler(INarrationService narration)
    : IRequestHandler<GetRecentNarrationQuery, Result<NarrationRecentResponse>>
{
    public Task<Result<NarrationRecentResponse>> Handle(
        GetRecentNarrationQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success(new NarrationRecentResponse(
            narration.GetRecentResults(request.Limit).Select(NarrationMappings.Map).ToArray(),
            narration.GetRecentEvents(request.Limit).Select(NarrationMappings.Map).ToArray())));
    }
}
