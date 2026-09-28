using System.Text;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Application.Interactions;

public sealed class AiResponseSanitizer(IOptions<InteractionOptions> options) : IAiResponseSanitizer
{
    private readonly int _maxCharacters = options.Value.MaxResponseCharacters;

    public AiResponseSanitizationResult Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AiResponseSanitizationResult(false, null, "AI_EMPTY_RESPONSE");
        }

        var normalized = string.Concat(text.Where(character =>
            !char.IsControl(character) || character is '\r' or '\n' or '\t')).Trim();
        if (normalized.Length == 0)
        {
            return new AiResponseSanitizationResult(false, null, "AI_EMPTY_RESPONSE");
        }

        var runes = normalized.EnumerateRunes().Take(_maxCharacters).ToArray();
        var sanitized = string.Concat(runes.Select(rune => rune.ToString()));
        return new AiResponseSanitizationResult(true, sanitized, null);
    }
}
