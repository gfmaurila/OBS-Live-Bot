using FluentValidation;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ObsLiveBot.Api.Configuration;
using ObsLiveBot.Api.Errors;
using ObsLiveBot.Api.Features.Obs.GetStatus;
using ObsLiveBot.Api.Features.Obs.LiveState;
using ObsLiveBot.Api.Features.Chat;
using ObsLiveBot.Api.Features.Interactions;
using ObsLiveBot.Api.Features.Narration;
using ObsLiveBot.Application.Features.Obs.GetStatus;
using ObsLiveBot.Application.Validation;
using ObsLiveBot.Infrastructure;
using ObsLiveBot.Infrastructure.Configuration;
using Serilog;
using Serilog.Formatting.Json;

DotEnv.LoadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
var providerConfigDirectory = Environment.GetEnvironmentVariable("GFM_STUDIOOS_CONFIG_PATH");
var providerConfigLoad = StudioOsProviderConfigurationLoader.Load(builder.Configuration, providerConfigDirectory);
var socialStreamConfigLoad = SocialStreamNinjaConfigurationLoader.Load(builder.Configuration, providerConfigDirectory);
builder.Host.UseSerilog((_, _, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter()));

builder.Services
    .AddOptions<ObsWebSocketOptions>()
    .Configure(options =>
    {
        var runningInContainer = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        options.Host = Environment.GetEnvironmentVariable("OBS_WEBSOCKET_HOST")
            ?? (runningInContainer ? "host.docker.internal" : "localhost");
        options.Port = int.TryParse(
            Environment.GetEnvironmentVariable("OBS_WEBSOCKET_PORT"),
            out var port)
            ? port
            : 4455;
        options.Password = Environment.GetEnvironmentVariable("OBS_WEBSOCKET_PASSWORD");
    })
    .ValidateOnStart();

builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetObsStatusQuery>();
    configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssemblyContaining<GetObsStatusQuery>(ServiceLifetime.Singleton);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddObsInfrastructure();
builder.Services.AddLiveChatInfrastructure(builder.Configuration);
builder.Services.AddInteractionInfrastructure(builder.Configuration);
builder.Services.AddNarrationInfrastructure(builder.Configuration);

var app = builder.Build();

if (providerConfigLoad.Loaded)
{
    app.Logger.LogInformation("STUDIOOS_PROVIDER_CONFIG_LOADED schemaVersion={SchemaVersion}", 1);
}
else
{
    app.Logger.LogWarning(
        "STUDIOOS_PROVIDER_CONFIG_UNAVAILABLE reason={Reason}; provider integrations remain disabled",
        providerConfigLoad.Status);
}

if (socialStreamConfigLoad.Loaded)
{
    app.Logger.LogInformation("STUDIOOS_SOCIALSTREAM_CONFIG_LOADED schemaVersion={SchemaVersion}", 1);
}
else
{
    app.Logger.LogWarning(
        "STUDIOOS_SOCIALSTREAM_CONFIG_UNAVAILABLE reason={Reason}; Social Stream Ninja remains disabled",
        socialStreamConfigLoad.Status);
}

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet(
    "/health",
    async (HealthCheckService healthCheckService, CancellationToken cancellationToken) =>
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        var obs = report.Entries.TryGetValue("obs", out var obsEntry)
            ? obsEntry.Status.ToString().ToLowerInvariant()
            : "unknown";
        var chat = report.Entries.TryGetValue("chat", out var chatEntry)
            ? chatEntry.Status.ToString().ToLowerInvariant()
            : "unknown";
        var interactions = report.Entries.TryGetValue("interactions", out var interactionEntry)
            ? interactionEntry.Status.ToString().ToLowerInvariant()
            : "unknown";
        var narration = report.Entries.TryGetValue("narration", out var narrationEntry)
            ? narrationEntry.Status.ToString().ToLowerInvariant()
            : "unknown";
        var status = report.Status switch
        {
            HealthStatus.Healthy => "healthy",
            HealthStatus.Degraded => "degraded",
            _ => "unhealthy"
        };

        return Results.Json(
            new { service = "obs-live-bot", status, obs, chat, interactions, narration },
            statusCode: report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK);
    });

app.MapObsStatusEndpoint();
app.MapObsLiveStateEndpoints();
app.MapLiveChatEndpoints(app.Environment.IsDevelopment());
app.MapInteractionEndpoints(app.Environment.IsDevelopment());
app.MapNarrationEndpoints(app.Environment.IsDevelopment());
app.Run();

public partial class Program;
