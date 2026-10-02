using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;
using ObsLiveBot.Domain.Interactions;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ObsLiveBot.Application.Interactions;

/// <summary>
/// Produces the deterministic text that the Chat voice reads aloud for an incoming message.
///
/// This component never asks a model to rewrite the message. It only removes the interaction trigger
/// and applies fixed, documented sanitization, so what is spoken is what the viewer typed.
/// </summary>
public sealed class ChatSpeechBuilder(
    IOptions<InteractionOptions> interactionOptions,
    IOptions<NarrationOptions> narrationOptions) : IChatSpeechBuilder
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    private const string UserNameToken = "{username}";
    private const string MessageToken = "{message}";

    // http(s)://... , www.... and bare scheme-less links. Deliberately not attempting to detect every
    // TLD: an over-eager pattern would delete ordinary words, and the goal is only to stop Piper from
    // reading a long raw URL aloud.
    private static readonly Regex UrlPattern = new(
        @"(?i)\b(?:https?://|www\.)\S+|\b[a-z0-9][a-z0-9\-]*(?:\.[a-z0-9\-]+)*\.(?:com|net|org|br|io|dev|app|gg|tv|co)\b(?:/\S*)?",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly char[] Whitespace = [' ', '\t', '\n', '\r'];

    private readonly InteractionOptions _interaction = interactionOptions.Value;
    private readonly ChatVoiceOptions _chat = narrationOptions.Value.ChatVoice;

    public ChatSpeechResult Build(LiveChatEvent chatEvent, InteractionDecision decision)
    {
        if (!_chat.Enabled)
            return new ChatSpeechResult(false, null, "CHAT_VOICE_DISABLED", _chat.VoiceId);

        var original = chatEvent.Message ?? string.Empty;
        var message = ChatTriggerText.Strip(original, _interaction);
        var (body, urlsReplaced) = SanitizeBody(message, out var truncated);
        if (string.IsNullOrWhiteSpace(body))
            return new ChatSpeechResult(false, null, "CHAT_SPEECH_EMPTY", _chat.VoiceId, original.Length);

        var text = _chat.SpeakUserName
            ? ComposeWithUserName(decision.UserDisplayName, body)
            : body;

        if (string.IsNullOrWhiteSpace(text))
            return new ChatSpeechResult(false, null, "CHAT_SPEECH_EMPTY", _chat.VoiceId, original.Length);

        return new ChatSpeechResult(true, text, null, _chat.VoiceId, original.Length, truncated, urlsReplaced);
    }

    private string ComposeWithUserName(string? userDisplayName, string body)
    {
        var name = SanitizeUserName(userDisplayName);
        if (string.IsNullOrEmpty(name)) return body;

        // The template is substituted literally rather than through string.Format: the tokens are
        // fixed ({username} and {message}) and a viewer-supplied name or message can never be
        // interpreted as a format specifier. NarrationOptionsValidator rejects any other token.
        return _chat.UserNameFormat
            .Replace(UserNameToken, name, StringComparison.Ordinal)
            .Replace(MessageToken, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Produces a speakable form of the display name. The stored chat identity is never modified; only
    /// this spoken representation is derived.
    /// </summary>
    private static string SanitizeUserName(string? userDisplayName)
    {
        if (string.IsNullOrWhiteSpace(userDisplayName)) return string.Empty;

        var builder = new StringBuilder(userDisplayName.Length);
        var lastWasSpace = false;
        foreach (var character in userDisplayName.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            // Keep letters, digits and the punctuation Piper can pronounce sensibly in a name.
            if (char.IsLetterOrDigit(character) || character is '.' or '_' or '-' or '\'' or '@')
                builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private (string Body, bool UrlsReplaced) SanitizeBody(string message, out bool truncated)
    {
        truncated = false;
        var urlsReplaced = false;

        // 1. Drop control and formatting characters, keeping ordinary punctuation and letters.
        var cleaned = new StringBuilder(message.Length);
        foreach (var character in message)
        {
            if (char.IsControl(character)) continue;
            if (char.IsSurrogate(character)) continue;
            cleaned.Append(character);
        }

        // 2. Replace URLs with a single spoken word instead of reading the raw address.
        var withLinks = UrlPattern.Replace(cleaned.ToString(), match =>
        {
            urlsReplaced = true;
            return _chat.UrlSpokenWord;
        });
        if (urlsReplaced) withLinks = WhitespacePattern.Replace(withLinks, " ");

        // 3. Collapse repeated punctuation so Piper does not spell out "exclamação" chains.
        var collapsed = CollapsePunctuation(withLinks);

        // 4. Collapse whitespace runs and trim.
        var body = WhitespacePattern.Replace(collapsed, " ").Trim();

        // 5. Enforce a maximum spoken length at a word boundary, without inventing content.
        var max = Math.Max(1, _chat.MaxMessageCharacters);
        if (body.Length > max)
        {
            body = TruncateAtWordBoundary(body, max);
            truncated = true;
        }

        return (body, urlsReplaced);
    }

    private static string CollapsePunctuation(string value)
    {
        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var character = value[index];
            var run = 1;
            while (index + run < value.Length && value[index + run] == character &&
                   character is '!' or '?' or '.' or ',' or '-' or '…')
                run++;

            builder.Append(character);
            index += run;
        }

        return builder.ToString();
    }

    private static string TruncateAtWordBoundary(string body, int max)
    {
        var cut = body.LastIndexOfAny(Whitespace, Math.Min(max, body.Length - 1));
        if (cut <= 0) cut = Math.Min(max, body.Length);
        return body[..cut].Trim();
    }
}
