using MediatR;
using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Application.ChatResponses;
using ObsLiveBot.Application.Features.ChatResponses.DevSend;
using ObsLiveBot.Application.Features.ChatResponses.Settings;
using ObsLiveBot.Contracts.Chat;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Api.Features.ChatResponses;

public static class ChatResponseEndpoints
{
    public static IEndpointRouteBuilder MapChatResponseEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool developmentMode)
    {
        endpoints.MapGet("/api/chat-responses/providers", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var capabilities = await sender.Send(new GetChatProviderCapabilitiesQuery(), cancellationToken);
                return Results.Ok(capabilities);
            })
            .WithName("GetChatProviderCapabilities")
            .WithTags("ChatResponses")
            .Produces<ChatProviderCapabilitiesResponse>();

        endpoints.MapGet("/api/chat-responses/settings", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var settings = await sender.Send(new GetChatResponseSettingsQuery(), cancellationToken);
                return Results.Ok(settings);
            })
            .WithName("GetChatResponseSettings")
            .WithTags("ChatResponses")
            .Produces<ChatResponseSettingsResponse>();

        endpoints.MapPut("/api/chat-responses/settings", async (
                UpdateChatResponseSettingsRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var settings = await sender.Send(
                    new UpdateChatResponseSettingsCommand(request.Enabled, request.Reset), cancellationToken);
                return Results.Ok(settings);
            })
            .WithName("UpdateChatResponseSettings")
            .WithTags("ChatResponses")
            .Produces<ChatResponseSettingsResponse>()
            .ProducesValidationProblem();

        endpoints.MapGet("/api/chat-responses/state", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var state = await sender.Send(new GetChatResponseStateQuery(), cancellationToken);
                return Results.Ok(Map(state));
            })
            .WithName("GetChatResponseState")
            .WithTags("ChatResponses")
            .Produces<ChatResponseStateResponse>();

        endpoints.MapGet("/api/chat-responses/recent", async (
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var results = await sender.Send(new GetRecentChatResponsesQuery(limit ?? 20), cancellationToken);
                return Results.Ok(results.Select(Map).ToArray());
            })
            .WithName("GetRecentChatResponses")
            .WithTags("ChatResponses")
            .Produces<ChatResponseRecordResponse[]>()
            .ProducesValidationProblem();

        endpoints.MapGet("/api/chat-responses/senders", async (
                ISender sender,
                IOptions<ChatResponseOptions> options,
                CancellationToken cancellationToken) =>
            {
                var selectedName = options.Value.Sender.Trim();
                var senders = await sender.Send(new GetChatResponseSendersQuery(), cancellationToken);
                return Results.Ok(new ChatResponseSendersResponse(
                    senders.Select(entry =>
                    {
                        var state = entry.GetRuntimeState();
                        return new ChatResponseSenderResponse(
                            entry.Name,
                            string.Equals(entry.Name, selectedName, StringComparison.OrdinalIgnoreCase),
                            entry.IsAvailable,
                            entry.IsDevelopment,
                            state.Status,
                            entry.SupportedProviders.Select(provider => provider.ToString()).ToArray(),
                            state.Requests,
                            state.Successes,
                            state.Failures,
                            state.LastSuccessAtUtc,
                            state.LastFailureAtUtc,
                            state.LastErrorCode);
                    }).ToArray()));
            })
            .WithName("GetChatResponseSenders")
            .WithTags("ChatResponses")
            .Produces<ChatResponseSendersResponse>();

        if (developmentMode)
        {
            // The one endpoint that can put StudioOS's own text in front of a live audience. It is
            // development-only and still obeys ChatResponses:Enabled, so writing at all remains a
            // deliberate act rather than something a stray request can cause on its own.
            endpoints.MapPost("/api/chat-responses/dev/send", async (
                    DevelopmentChatResponseRequest request,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var response = await sender.Send(
                        new SendDevelopmentChatResponseCommand(request.Provider, request.ChannelId, request.Text),
                        cancellationToken);
                    if (response.Success)
                        return Results.Ok(response);
                    return Results.Problem(
                        title: "Written chat response was refused.",
                        detail: response.Reason,
                        statusCode: StatusCodes.Status409Conflict);
                })
                .WithName("SendDevelopmentChatResponse")
                .WithTags("ChatResponses")
                .Produces<DevelopmentChatResponseResponse>()
                .ProducesValidationProblem();
        }

        return endpoints;
    }

    private static ChatResponseStateResponse Map(ChatResponseStateSnapshot state) =>
        new(
            state.Enabled,
            state.Status,
            state.SelectedSender,
            state.SenderStatus,
            state.SenderAvailable,
            state.QueueDepth,
            state.QueueCapacity,
            state.Queued,
            state.Sent,
            state.Skipped,
            state.Failed,
            state.Cancelled,
            state.Rejected,
            state.HistorySize,
            state.HistoryCapacity,
            state.CooldownEntries,
            state.CooldownCapacity,
            state.IdempotencyEntries,
            state.IdempotencyCapacity,
            state.EchoEntries,
            state.EchoCapacity,
            state.LastSentAtUtc,
            state.LastFailureAtUtc,
            state.LastFailureReason);

    private static ChatResponseRecordResponse Map(ChatResponseResult result) =>
        new(
            result.ChatResponseId,
            result.InteractionId,
            result.SourceMessageId,
            result.Provider.ToString(),
            result.ChannelId,
            result.UserId,
            result.UserName,
            result.SenderName,
            result.Status.ToString(),
            result.Reason,
            result.CreatedAtUtc,
            result.CompletedAtUtc,
            result.Sequence,
            result.CorrelationId,
            result.ResponseText,
            result.CharacterCount,
            result.IdempotencyKey,
            result.Simulated,
            result.DurationMilliseconds,
            result.Attempt);
}