using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Infrastructure.Configuration;
using ObsLiveBot.Infrastructure.ChatResponses;

namespace ObsLiveBot.UnitTests.Configuration;

/// <summary>
/// Validates the configuration that actually ships, not a hand-built object graph. A wrong section
/// name or a misspelled key binds silently to a default, so this is the only check that notices a
/// setting the operator believes is applied but is not.
/// </summary>
public sealed class ShippedConfigurationTests
{
    [Fact]
    public void ShippedAppsettings_BindsBothVoiceRolesToTheExpectedVoices()
    {
        var (interactions, narration) = Bind();

        Assert.Equal("pt_BR-jeff-medium", narration.ChatVoice.VoiceId);
        Assert.Equal("pt_BR-faber-medium", narration.AssistantVoice.VoiceId);
        Assert.True(narration.ChatVoice.Enabled);
        Assert.True(narration.AssistantVoice.Enabled);
    }

    [Fact]
    public void ShippedAppsettings_PassesTheNarrationOptionsValidator()
    {
        var (interactions, narration) = Bind();

        var result = new NarrationOptionsValidator(Options.Create(interactions)).Validate(
            NarrationOptions.SectionName, narration);

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void ShippedAppsettings_PassesTheInteractionOptionsValidator()
    {
        var (interactions, _) = Bind();

        var result = new InteractionOptionsValidator().Validate(
            InteractionOptions.SectionName, interactions);

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void ShippedAppsettings_PassesTheChatResponseOptionsValidator()
    {
        var chatResponses = BindChatResponses();

        var result = new ChatResponseOptionsValidator().Validate(
            ChatResponseOptions.SectionName, chatResponses);

        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void WrittenChatResponsesStayDisabledInTheShippedConfiguration()
    {
        var chatResponses = BindChatResponses();

        // This is the switch that lets StudioOS type into a live chat on its own. Like the audible one it
        // ships off, because the reply appears in front of an audience the moment it is enabled.
        Assert.False(chatResponses.Enabled);

        // The deterministic sender may be selected, but only explicitly - it is never a silent fallback,
        // so shipping it as the default sender would be a fallback by another name.
        Assert.Equal("SocialStreamNinja", chatResponses.Sender);
        Assert.Empty(chatResponses.AllowedProviders);
        Assert.Empty(chatResponses.SelfActorIdentities);
    }

    [Fact]
    public void AutomaticNarrationStaysDisabledInTheShippedConfiguration()
    {
        var (_, narration) = Bind();

        // This is the switch that makes the bot speak by itself. It must ship off: audible playback
        // only happens after the analyst deliberately authorizes it.
        Assert.False(narration.AutoPlayInteractions);
        Assert.False(Bind().Interactions.AutoPlayInteractions);
    }

    [Fact]
    public void ShippedAppsettings_PointsTheVoiceCatalogAtTheMountedVoiceDirectory()
    {
        var (interactions, _) = Bind();

        Assert.Equal("/opt/tts-engine/voices", interactions.Tts.VoicesDirectory);
    }

    [Fact]
    public void ShippedAppsettings_KeepsTheLegacyTtsVoiceAsTheAssistantVoice()
    {
        var (interactions, narration) = Bind();

        // Interactions:Tts:Voice predates dual voice and still means the assistant voice, so it must
        // not drift away from the configured Assistant role.
        Assert.Equal(interactions.Tts.Voice, narration.AssistantVoice.VoiceId);
        Assert.Equal("pt_BR-faber-medium", interactions.Tts.Voice);
    }

    [Fact]
    public void TheChatVoiceTemplateUsesOnlyTheSupportedTokens()
    {
        var (_, narration) = Bind();

        Assert.Equal("{username} disse: {message}", narration.ChatVoice.UserNameFormat);
    }

    private static ChatResponseOptions BindChatResponses() =>
        Configuration().GetSection(ChatResponseOptions.SectionName).Get<ChatResponseOptions>()
        ?? new ChatResponseOptions();

    private static (InteractionOptions Interactions, NarrationOptions Narration) Bind()
    {
        var configuration = Configuration();

        var interactions = configuration
            .GetSection(InteractionOptions.SectionName).Get<InteractionOptions>() ?? new InteractionOptions();
        var narration = configuration
            .GetSection(NarrationOptions.SectionName).Get<NarrationOptions>() ?? new NarrationOptions();
        return (interactions, narration);
    }

    private static IConfigurationRoot Configuration() =>
        new ConfigurationBuilder()
            .SetBasePath(RepositoryRoot())
            .AddJsonFile("src/dotnet/ObsLiveBot.Api/appsettings.json", optional: false)
            .Build();

    /// <summary>
    /// Walks up from the test binaries to the repository root so the real file is read, not a build copy.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(
                   Path.Combine(directory.FullName, "src", "dotnet", "ObsLiveBot.Api", "appsettings.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository appsettings.json could not be located.");
    }
}
