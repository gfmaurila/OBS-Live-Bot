using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.Features.Chat.Ingest;

public sealed record IngestLiveChatEventCommand(ProviderLiveChatEvent ProviderEvent)
    : IRequest<Result<LiveChatIngestionResult>>;

public sealed class ProviderLiveChatEventValidator : AbstractValidator<ProviderLiveChatEvent>
{
    private static readonly string[] ForbiddenMetadataTerms = ["password", "token", "secret", "credential", "oauth"];

    public ProviderLiveChatEventValidator(IOptions<LiveChatOptions> options)
    {
        var limits = options.Value;
        RuleFor(item => item.Provider)
            .Must(provider => provider != LiveChatProviderType.Unknown && Enum.IsDefined(provider));
        RuleFor(item => item.EventType).Must(Enum.IsDefined);
        RuleFor(item => item.TimestampUtc).NotEqual(default(DateTimeOffset));
        RuleFor(item => item.User).NotNull();
        RuleFor(item => item.User.Provider).Equal(item => item.Provider)
            .When(item => item.User is not null);
        RuleFor(item => item.User.UserId).NotEmpty().MaximumLength(limits.MaxUserIdLength)
            .When(item => item.User is not null);
        RuleFor(item => item.User.Username).MaximumLength(limits.MaxTextFieldLength)
            .When(item => item.User is not null);
        RuleFor(item => item.User.DisplayName).MaximumLength(limits.MaxTextFieldLength)
            .When(item => item.User is not null);
        RuleFor(item => item.User.Badges)
            .Must(badges => badges.Count <= limits.MaxBadges && badges.All(badge => badge.Length <= limits.MaxBadgeLength))
            .When(item => item.User is not null)
            .WithMessage("User badges exceed the configured payload limits.");
        RuleFor(item => item.ProviderEventId).MaximumLength(limits.MaxProviderEventIdLength);
        RuleFor(item => item.ChannelId).MaximumLength(limits.MaxChannelIdLength);
        RuleFor(item => item.ChannelName).MaximumLength(limits.MaxTextFieldLength);
        RuleFor(item => item.Message).NotEmpty().MaximumLength(limits.MaxMessageLength)
            .When(item => item.EventType == LiveChatEventType.Message);
        RuleFor(item => item.Metadata).Cascade(CascadeMode.Stop).NotNull()
            .Must(metadata => metadata.Count <= limits.MaxMetadataEntries)
            .WithMessage($"Metadata cannot exceed {limits.MaxMetadataEntries} entries.")
            .Must(metadata => metadata.Keys.All(key => key.Length <= limits.MaxMetadataKeyLength))
            .WithMessage($"Metadata keys cannot exceed {limits.MaxMetadataKeyLength} characters.")
            .Must(metadata => metadata.All(pair => pair.Value is null || pair.Value.Length <= limits.MaxMetadataValueLength))
            .WithMessage($"Metadata values cannot exceed {limits.MaxMetadataValueLength} characters.")
            .Must(metadata => metadata.Keys.All(key => !ForbiddenMetadataTerms.Any(
                term => key.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .WithMessage("Metadata contains a forbidden sensitive key.");
    }
}

public sealed class IngestLiveChatEventCommandValidator : AbstractValidator<IngestLiveChatEventCommand>
{
    public IngestLiveChatEventCommandValidator(IValidator<ProviderLiveChatEvent> providerEventValidator) =>
        RuleFor(command => command.ProviderEvent).SetValidator(providerEventValidator);
}

public sealed class IngestLiveChatEventCommandHandler(ILiveChatIngestionPipeline pipeline)
    : IRequestHandler<IngestLiveChatEventCommand, Result<LiveChatIngestionResult>>
{
    public async Task<Result<LiveChatIngestionResult>> Handle(
        IngestLiveChatEventCommand request,
        CancellationToken cancellationToken) =>
        Result.Success(await pipeline.IngestAsync(request.ProviderEvent, cancellationToken).ConfigureAwait(false));
}
