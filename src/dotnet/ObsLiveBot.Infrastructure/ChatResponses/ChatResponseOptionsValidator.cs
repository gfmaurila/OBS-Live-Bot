using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// Rejects a written-reply configuration that cannot work, at startup, before anything is written.
///
/// The rules that matter are the character ceiling, the message length ceiling and the sender name. Each
/// exists because a value outside it fails only at the moment a reply is being sent - that is, in front of
/// a live audience - while catching it at startup costs a restart.
/// </summary>
public sealed class ChatResponseOptionsValidator : IValidateOptions<ChatResponseOptions>
{
    private static readonly string[] KnownSenders = ["SocialStreamNinja", "Development"];

    public ValidateOptionsResult Validate(string? name, ChatResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Sender) ||
            !KnownSenders.Contains(options.Sender.Trim(), StringComparer.OrdinalIgnoreCase))
            failures.Add(
                $"ChatResponses:Sender must be one of {string.Join(", ", KnownSenders)}. " +
                $"Received '{options.Sender}'.");

        if (options.MaxCharacters is < 1 or > SocialStreamNinjaChatWriteClient.MaxFillCharacters)
            failures.Add(
                $"ChatResponses:MaxCharacters must be between 1 and " +
                $"{SocialStreamNinjaChatWriteClient.MaxFillCharacters}, which is the largest fill Social " +
                "Stream Ninja accepts. Received " + options.MaxCharacters + ".");

        if (options.MaxQueueSize < 1)
            failures.Add($"ChatResponses:MaxQueueSize must be at least 1. Received {options.MaxQueueSize}.");

        if (options.GlobalCooldownSeconds < 0)
            failures.Add($"ChatResponses:GlobalCooldownSeconds cannot be negative.");

        if (options.UserCooldownSeconds < 0)
            failures.Add($"ChatResponses:UserCooldownSeconds cannot be negative.");

        if (options.CooldownCapacity < 1)
            failures.Add($"ChatResponses:CooldownCapacity must be at least 1.");

        if (options.IdempotencyCapacity < 1)
            failures.Add($"ChatResponses:IdempotencyCapacity must be at least 1.");

        if (options.EchoCapacity < 1)
            failures.Add($"ChatResponses:EchoCapacity must be at least 1.");

        if (options.EchoWindowSeconds < 0)
            failures.Add("ChatResponses:EchoWindowSeconds cannot be negative.");

        if (options.HistoryCapacity < 1)
            failures.Add($"ChatResponses:HistoryCapacity must be at least 1.");

        if (options.CommandTimeoutSeconds < 1)
            failures.Add($"ChatResponses:CommandTimeoutSeconds must be at least 1.");

        // A prefix longer than the whole reply budget would leave nothing but the prefix to send.
        if (options.MessagePrefix.Length > options.MaxCharacters)
            failures.Add(
                "ChatResponses:MessagePrefix is longer than ChatResponses:MaxCharacters, so no reply " +
                "text could ever be written.");

        foreach (var provider in options.AllowedProviders ?? [])
        {
            // Unknown is a defined enum member but not a platform: nothing can ever resolve a source
            // for it, so permitting it here would only create a reply that is always silently skipped.
            if (!Enum.IsDefined(provider) || provider == LiveChatProviderType.Unknown)
                failures.Add($"ChatResponses:AllowedProviders contains an unusable provider '{provider}'.");
        }

        // An empty allow-list is the shipped default and means "any platform the selected sender supports".
        // It is not an error: requiring one entry would make the safe default unstartable, and the gate
        // already refuses any provider the selected sender does not support.

        if (string.Equals(options.Sender, "Development", StringComparison.OrdinalIgnoreCase) &&
            !options.AllowDevelopmentSender)
            failures.Add(
                "ChatResponses:Sender is 'Development' while ChatResponses:AllowDevelopmentSender is false.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
