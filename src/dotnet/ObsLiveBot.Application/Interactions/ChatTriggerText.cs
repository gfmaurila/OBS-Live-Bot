using ObsLiveBot.Application.Abstractions;
using System.Text.RegularExpressions;

namespace ObsLiveBot.Application.Interactions;

/// <summary>
/// Removes the interaction trigger from a chat message using exactly the same matching rules that
/// <c>InteractionDecisionPolicy</c> uses to decide that the message is a trigger. The AI context and
/// the spoken chat text both depend on this, so they cannot drift apart.
///
/// This is deliberately not a global string replacement: the command prefix is only removed from the
/// start of the message, and a mention is only removed when it appears as a standalone word. Text
/// that merely contains a similar word is left untouched.
/// </summary>
public static class ChatTriggerText
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    public static string Strip(string message, InteractionOptions options)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        var command = options.ReservedCommandPrefix;
        if (!string.IsNullOrEmpty(command) &&
            message.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return message[command.Length..].Trim();

        foreach (var mention in options.BotMentionTriggers.OrderByDescending(value => value.Length))
        {
            if (string.IsNullOrWhiteSpace(mention)) continue;
            var pattern =
                $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(mention)}(?![\p{{L}}\p{{N}}_])[:,]?\s*";
            message = Regex.Replace(
                message,
                pattern,
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }

        return message.Trim();
    }
}
