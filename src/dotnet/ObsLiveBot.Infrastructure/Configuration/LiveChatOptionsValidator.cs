using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class LiveChatOptionsValidator : IValidateOptions<LiveChatOptions>
{
    public ValidateOptionsResult Validate(string? name, LiveChatOptions options)
    {
        var failures = new List<string>();
        if (options.BufferCapacity is < 1 or > 10_000) failures.Add("LiveChat:BufferCapacity must be between 1 and 10000.");
        if (options.DeduplicationCapacity is < 1 or > 100_000) failures.Add("LiveChat:DeduplicationCapacity must be between 1 and 100000.");
        if (options.MaxMessageLength is < 1 or > 100_000) failures.Add("LiveChat:MaxMessageLength must be between 1 and 100000.");
        if (options.MaxProviderEventIdLength is < 1 or > 4_096) failures.Add("LiveChat:MaxProviderEventIdLength is invalid.");
        if (options.MaxChannelIdLength is < 1 or > 4_096) failures.Add("LiveChat:MaxChannelIdLength is invalid.");
        if (options.MaxUserIdLength is < 1 or > 4_096) failures.Add("LiveChat:MaxUserIdLength is invalid.");
        if (options.MaxTextFieldLength is < 1 or > 10_000) failures.Add("LiveChat:MaxTextFieldLength is invalid.");
        if (options.MaxBadges is < 0 or > 256) failures.Add("LiveChat:MaxBadges must be between 0 and 256.");
        if (options.MaxBadgeLength is < 1 or > 4_096) failures.Add("LiveChat:MaxBadgeLength is invalid.");
        if (options.MaxMetadataEntries is < 0 or > 256) failures.Add("LiveChat:MaxMetadataEntries must be between 0 and 256.");
        if (options.MaxMetadataKeyLength is < 1 or > 4_096) failures.Add("LiveChat:MaxMetadataKeyLength is invalid.");
        if (options.MaxMetadataValueLength is < 1 or > 100_000) failures.Add("LiveChat:MaxMetadataValueLength is invalid.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
