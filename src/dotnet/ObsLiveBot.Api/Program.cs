using Microsoft.Extensions.Diagnostics.HealthChecks;
using ObsLiveBot.Api.Configuration;
using ObsLiveBot.Api.Features.Obs.GetStatus;
using ObsLiveBot.Application.Features.Obs.GetStatus;
using ObsLiveBot.Application.Mediator;
using ObsLiveBot.Contracts.Obs;
using ObsLiveBot.Infrastructure;
using ObsLiveBot.Infrastructure.Configuration;

DotEnv.LoadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

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

builder.Services.AddSingleton<IMediator, ServiceProviderMediator>();
builder.Services.AddSingleton<IQueryHandler<GetObsStatusQuery, ObsStatusResponse>, GetObsStatusQueryHandler>();
builder.Services.AddObsInfrastructure();

var app = builder.Build();

app.MapGet(
    "/health",
    async (HealthCheckService healthCheckService, CancellationToken cancellationToken) =>
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        var obs = report.Entries.TryGetValue("obs", out var obsEntry)
            ? obsEntry.Status.ToString().ToLowerInvariant()
            : "unknown";
        var status = report.Status switch
        {
            HealthStatus.Healthy => "healthy",
            HealthStatus.Degraded => "degraded",
            _ => "unhealthy"
        };

        return Results.Json(
            new { service = "obs-live-bot", status, obs },
            statusCode: report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK);
    });

app.MapObsStatusEndpoint();
app.Run();

public partial class Program;
