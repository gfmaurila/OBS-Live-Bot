namespace ObsLiveBot.Application.YouTube;

/// <summary>
/// Configuration for automatic YouTube live discovery and SSN source ownership.
/// Contains only public, non-secret values: a channel handle/ID and bounded transport limits.
/// Precedence: an explicit per-live manual URL always wins, otherwise discovery owns the lifecycle.
/// </summary>
public sealed class YouTubeLiveDiscoveryOptions
{
    public const string SectionName = "YouTubeLiveDiscovery";

    /// <summary>Enables automatic discovery. Ignored while a manual live chat URL override is configured.</summary>
    public bool Enabled { get; set; }

    /// <summary>Channel handle (for example <c>@gfmaurila</c> or <c>gfmaurila</c>) or a <c>UC...</c> channel ID.</summary>
    public string? Channel { get; set; }

    /// <summary>Explicit per-live override. When set, StudioOS does not perform automatic discovery.</summary>
    public string? ManualLiveChatUrl { get; set; }

    /// <summary>Stops the canonical source when the live ends, preserving the SSN record for audit.</summary>
    public bool AutoReleaseOnEnd { get; set; } = true;

    /// <summary>Minimum spacing between discovery attempts while already streaming, preventing hot loops.</summary>
    public int MinDiscoveryIntervalSeconds { get; set; } = 30;

    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>Hard cap on the public page response read into memory.</summary>
    public int MaxResponseBytes { get; set; } = 4 * 1024 * 1024;
}
