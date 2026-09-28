using FluentValidation;
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ObsLiveBot.Api.Configuration;
using ObsLiveBot.Api.Features.Obs.GetStatus;
using ObsLiveBot.Application.Features.Obs.GetStatus;
using ObsLiveBot.Application.Validation;
using ObsLiveBot.Infrastructure;
using ObsLiveBot.Infrastructure.Configuration;
using Serilog;
using Serilog.Formatting.Json;

DotEnv.LoadIfPresent(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
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
builder.Services.AddValidatorsFromAssemblyContaining<GetObsStatusQuery>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddObsInfrastructure();

var app = builder.Build();

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
