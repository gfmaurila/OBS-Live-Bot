using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Infrastructure.Interactions;

namespace ObsLiveBot.UnitTests.Interactions;

/// <summary>
/// A request selects a voice by name, never by path. These tests pin that boundary: a voice id either
/// names a model that is installed under the voices directory, or it resolves to nothing.
/// </summary>
public sealed class PiperVoiceCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "gfm-voices-" + Guid.NewGuid().ToString("N"));
    private readonly string _voices;

    public PiperVoiceCatalogTests()
    {
        _voices = Path.Combine(_root, "voices");
        Directory.CreateDirectory(_voices);
        Install("pt_BR-faber-medium");
        Install("pt_BR-jeff-medium");
    }

    [Fact]
    public void InstalledVoice_ResolvesToItsModelAndConfig()
    {
        var resolved = Catalog().Resolve("pt_BR-jeff-medium");

        Assert.NotNull(resolved);
        Assert.Equal("pt_BR-jeff-medium", resolved!.VoiceId);
        Assert.EndsWith("pt_BR-jeff-medium.onnx", resolved.ModelPath, StringComparison.Ordinal);
        Assert.Equal(resolved.ModelPath + ".json", resolved.ConfigPath);
        Assert.True(File.Exists(resolved.ModelPath));
    }

    [Fact]
    public void TheTwoRolesResolveToDifferentModels()
    {
        var catalog = Catalog();
        var chat = catalog.Resolve("pt_BR-jeff-medium");
        var assistant = catalog.Resolve("pt_BR-faber-medium");

        Assert.NotNull(chat);
        Assert.NotNull(assistant);
        Assert.NotEqual(chat!.ModelPath, assistant!.ModelPath, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingVoiceId_FallsBackToTheConfiguredDefault(string? voiceId)
    {
        var resolved = Catalog().Resolve(voiceId);

        Assert.NotNull(resolved);
        Assert.Equal("pt_BR-faber-medium", resolved!.VoiceId);
    }

    [Fact]
    public void VoiceThatIsNotInstalled_DoesNotResolve()
    {
        Assert.Null(Catalog().Resolve("pt_BR-inexistente-medium"));
    }

    [Fact]
    public void ModelWithoutConfig_DoesNotResolve()
    {
        File.WriteAllBytes(Path.Combine(_voices, "pt_BR-sozinha-medium.onnx"), [1]);

        Assert.Null(Catalog().Resolve("pt_BR-sozinha-medium"));
    }

    [Theory]
    [InlineData("../pt_BR-jeff-medium")]
    [InlineData("..\\pt_BR-jeff-medium")]
    [InlineData("sub/pt_BR-jeff-medium")]
    [InlineData("pt_BR-jeff-medium.onnx")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\\\Windows\\\\System32\\\\drivers")]
    [InlineData("pt BR jeff")]
    [InlineData("pt_BR-jeff-medium;rm")]
    public void VoiceIdThatIsAPathOrInjection_DoesNotResolve(string voiceId)
    {
        Assert.Null(Catalog().Resolve(voiceId));
    }

    [Fact]
    public void VoiceIdLongerThanTheLimit_DoesNotResolve()
    {
        Assert.Null(Catalog().Resolve(new string('a', 65)));
    }

    [Fact]
    public void InstalledVoices_AreListedWithoutConfiglessModels()
    {
        File.WriteAllBytes(Path.Combine(_voices, "pt_BR-sem-config.onnx"), [1]);

        var installed = Catalog().ListInstalled();

        Assert.Equal(["pt_BR-faber-medium", "pt_BR-jeff-medium"], installed);
    }

    [Fact]
    public void MissingVoicesDirectory_ListsNothingAndResolvesNothing()
    {
        var options = Options.Create(new InteractionOptions());
        var value = options.Value;
        value.Tts.VoicesDirectory = Path.Combine(_root, "ausente");
        var catalog = new PiperVoiceCatalog(
            Options.Create(value), NullLogger<PiperVoiceCatalog>.Instance);

        Assert.Empty(catalog.ListInstalled());
        Assert.Null(catalog.Resolve("pt_BR-jeff-medium"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private PiperVoiceCatalog Catalog()
    {
        var value = new InteractionOptions();
        value.Tts.VoicesDirectory = _voices;
        return new PiperVoiceCatalog(Options.Create(value), NullLogger<PiperVoiceCatalog>.Instance);
    }

    private void Install(string voiceId)
    {
        File.WriteAllBytes(Path.Combine(_voices, voiceId + ".onnx"), [1]);
        // Piper derives the config path as "model path + .json", so the file on disk is .onnx.json.
        File.WriteAllText(Path.Combine(_voices, voiceId + ".onnx.json"), "{}");
    }
}
