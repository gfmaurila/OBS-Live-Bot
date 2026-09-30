using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.Features.Interactions.Settings;

namespace ObsLiveBot.UnitTests.Interactions;

public sealed class InteractionSettingsTests
{
    [Fact]
    public async Task DefaultSettings_AreSafeAndManualDisableClosesBothGates()
    {
        var interaction = new InteractionOptions();
        var narration = new NarrationOptions();
        Assert.False(interaction.AutoPlayInteractions);

        var handler = new UpdateInteractionSettingsCommandHandler(
            Options.Create(interaction), Options.Create(narration));
        await handler.Handle(new(true, "!studio", 10, 30), default);
        Assert.True(interaction.AutoPlayInteractions);
        Assert.True(narration.AutoPlayInteractions);

        await handler.Handle(new(false, "!studio", 10, 30), default);
        Assert.False(interaction.AutoPlayInteractions);
        Assert.False(narration.AutoPlayInteractions);
    }

    [Fact]
    public void SettingsValidator_RejectsUnsafeTriggerAndCooldownValues()
    {
        var validator = new UpdateInteractionSettingsCommandValidator();
        Assert.True(validator.Validate(new UpdateInteractionSettingsCommand(false, "!studio", 10, 30)).IsValid);
        Assert.False(validator.Validate(new UpdateInteractionSettingsCommand(false, "!", 10, 30)).IsValid);
        Assert.False(validator.Validate(new UpdateInteractionSettingsCommand(false, "studio", -1, 0)).IsValid);
        Assert.False(validator.Validate(new UpdateInteractionSettingsCommand(false, "!" + new string('x', 40), 10, 30)).IsValid);
    }
}
