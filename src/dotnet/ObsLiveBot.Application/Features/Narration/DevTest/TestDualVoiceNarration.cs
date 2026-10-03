using Ardalis.Result;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Contracts.Narration;
using ObsLiveBot.Domain.Interactions;

namespace ObsLiveBot.Application.Features.Narration.DevTest;

/// <summary>
/// Speaks one interaction's two clips on demand, without going through chat or the model.
///
/// This exists so an operator can verify the real voices, the real ordering and the real OBS route
/// on a quiet machine. It is the only caller of
/// <see cref="IDualVoiceNarrationCoordinator.SubmitDevelopmentTestAsync"/>, the automatic path can
/// never reach it, and it deliberately leaves <c>Narration:AutoPlayInteractions</c> exactly as
/// configured so a development test never leaves autoplay switched on.
/// </summary>
public sealed record TestDualVoiceNarrationCommand(string ChatText, string AssistantText)
    : IRequest<Result<NarrationDualVoiceDevTestResponse>>;

public sealed class TestDualVoiceNarrationCommandValidator : AbstractValidator<TestDualVoiceNarrationCommand>
{
    public TestDualVoiceNarrationCommandValidator(IOptions<InteractionOptions> options)
    {
        var limit = options.Value.Tts.MaxInputCharacters;
        RuleFor(command => command.ChatText).NotEmpty().MaximumLength(limit);
        RuleFor(command => command.AssistantText).NotEmpty().MaximumLength(limit);
    }
}

public sealed class TestDualVoiceNarrationCommandHandler(
    IInteractionProviderRegistry providers,
    IDualVoiceNarrationCoordinator coordinator,
    IOptions<InteractionOptions> interactionOptions,
    IOptions<NarrationOptions> narrationOptions)
    : IRequestHandler<TestDualVoiceNarrationCommand, Result<NarrationDualVoiceDevTestResponse>>
{
    /// <summary>
    /// Acceptance key for a manually requested group. It is process wide and monotonic so repeated
    /// tests keep the same relative order the automatic path would have given them, instead of
    /// colliding on one reused key and being refused as duplicates.
    /// </summary>
    private static long _sequence;

    public async Task<Result<NarrationDualVoiceDevTestResponse>> Handle(
        TestDualVoiceNarrationCommand request,
        CancellationToken cancellationToken)
    {
        var narration = narrationOptions.Value;
        if (!narration.Enabled)
            return Rejected("NARRATION_DISABLED");

        var provider = providers.GetTtsProvider();
        if (provider is null || provider.IsDevelopment ||
            !await provider.CheckAvailabilityAsync(cancellationToken).ConfigureAwait(false))
            return Rejected("REAL_TTS_UNAVAILABLE");

        var interactionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString("N");
        var chatVoice = ResolveVoice(narration.ChatVoice.VoiceId);
        var assistantVoice = ResolveVoice(narration.AssistantVoice.VoiceId);

        // Both halves start together, and the group is handed over while the assistant clip is still
        // being synthesized. That is the real shape of an interaction: the chat clip is playable
        // while the reply is still being voiced.
        var chatSynthesis = provider.SynthesizeAsync(Request(
            interactionId, request.ChatText, chatVoice, correlationId, ChatArtifact(interactionId)),
            cancellationToken);
        var assistantSynthesis = provider.SynthesizeAsync(Request(
            interactionId, request.AssistantText, assistantVoice, correlationId, AssistantArtifact(interactionId)),
            cancellationToken);

        var chat = await chatSynthesis.ConfigureAwait(false);
        if (!IsPlayable(chat))
            return Rejected(chat?.ErrorCode ?? "CHAT_TTS_FAILED", interactionId, correlationId, provider.Name);

        var sequence = Interlocked.Increment(ref _sequence);
        await coordinator.SubmitDevelopmentTestAsync(
            new DualVoiceNarrationRequest(
                interactionId,
                sequence,
                correlationId,
                Task.FromResult<TextToSpeechResult?>(chat),
                AsOptionalAsync(assistantSynthesis),
                TimeSpan.Zero),
            cancellationToken).ConfigureAwait(false);

        var assistant = await assistantSynthesis.ConfigureAwait(false);
        if (!IsPlayable(assistant))
        {
            // The chat half is already queued and still plays: a missing reply is not a failed test.
            return Result.Success(new NarrationDualVoiceDevTestResponse(
                true, assistant?.ErrorCode, interactionId, correlationId, sequence, provider.Name,
                chatVoice, assistantVoice, chat!.AudioDuration?.TotalSeconds, null));
        }

        return Result.Success(new NarrationDualVoiceDevTestResponse(
            true, null, interactionId, correlationId, sequence, provider.Name,
            chatVoice, assistantVoice,
            chat!.AudioDuration?.TotalSeconds, assistant!.AudioDuration?.TotalSeconds));
    }

    private string ResolveVoice(string? roleVoiceId) =>
        string.IsNullOrWhiteSpace(roleVoiceId)
            ? interactionOptions.Value.Tts.Voice
            : roleVoiceId;

    /// <summary>
    /// Presents a role's synthesis under the shape the coordinator expects, where a null result means
    /// "this clip does not exist". The underlying task is still awaited separately for the report, so
    /// this only changes what the coordinator sees.
    /// </summary>
    private static async Task<TextToSpeechResult?> AsOptionalAsync(Task<TextToSpeechResult> synthesis) =>
        await synthesis.ConfigureAwait(false);

    private TextToSpeechRequest Request(
        Guid interactionId,
        string text,
        string voice,
        string correlationId,
        Guid artifactId) =>
        new(
            interactionId, text, voice,
            interactionOptions.Value.Language, correlationId, artifactId);

    /// <summary>
    /// A clip is playable only when synthesis really produced audio. A simulated or silent result must
    /// never be handed to the narrator, because a test that cannot be heard proves nothing.
    /// </summary>
    private static bool IsPlayable(TextToSpeechResult? result) =>
        result is { Success: true, IsSimulated: false } &&
        !string.IsNullOrWhiteSpace(result.AudioPath) &&
        result.AudioDuration is not null;

    private static NarrationDualVoiceDevTestResponse Rejected(
        string errorCode,
        Guid? interactionId = null,
        string? correlationId = null,
        string? provider = null) =>
        new(false, errorCode, interactionId ?? Guid.Empty, correlationId ?? string.Empty, 0,
            provider ?? "Unavailable", string.Empty, string.Empty, null, null);

    // The two roles of one group must target two different WAV files, so the role is folded into the
    // guid bits instead of being appended outside them.
    private static Guid ChatArtifact(Guid interactionId) => Derive(interactionId, 0x5A);

    private static Guid AssistantArtifact(Guid interactionId) => Derive(interactionId, 0xA5);

    private static Guid Derive(Guid interactionId, byte roleTag)
    {
        Span<byte> bytes = stackalloc byte[16];
        interactionId.TryWriteBytes(bytes);
        bytes[15] ^= roleTag;
        return new Guid(bytes);
    }
}