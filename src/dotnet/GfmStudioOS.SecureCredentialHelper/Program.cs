using System.Net;
using System.Security.Cryptography;
using System.Text;
using GfmStudioOS.SecureCredentialHelper;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("GFM StudioOS Secure Credential Helper runs on Windows only.");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(server =>
{
    server.AddServerHeader = false;
    server.Limits.MaxRequestBodySize = 20 * 1024;
    server.ListenAnyIP(builder.Configuration.GetValue("CredentialHelper:Port", 51823), listen =>
        listen.Protocols = HttpProtocols.Http1);
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.MaxDepth = 4);
builder.Services.AddOptions<CredentialHelperOptions>()
    .Bind(builder.Configuration.GetSection(CredentialHelperOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CredentialHelperOptions>, CredentialHelperOptionsValidator>();
builder.Services.AddSingleton<ICredentialProtector, DpapiCurrentUserProtector>();
builder.Services.AddSingleton<ISecureCredentialStore>(services =>
{
    var options = services.GetRequiredService<IOptions<CredentialHelperOptions>>().Value;
    var path = options.StorageDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GFM StudioOS", "SecureCredentials");
    return new ProtectedFileCredentialStore(
        services.GetRequiredService<ICredentialProtector>(), path);
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    var options = context.RequestServices.GetRequiredService<IOptions<CredentialHelperOptions>>().Value;
    if (!CredentialHelperRequestAuthorizer.IsAllowed(
            context.Connection.RemoteIpAddress, options.AllowedClientNetworks) ||
        !CredentialHelperRequestAuthorizer.IsAuthorized(
            context.Request.Headers.Authorization, options.SharedKey))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    await next(context).ConfigureAwait(false);
});

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/v1/credentials/{provider}/{key}", async (
    string provider,
    string key,
    ISecureCredentialStore store,
    CancellationToken cancellationToken) =>
{
    if (!CredentialNameValidator.IsValidProvider(provider) || !CredentialNameValidator.IsValidKey(key))
        return Results.BadRequest(new { error = "invalid_credential_name" });
    try
    {
        var value = await store.ReadAsync(provider, key, cancellationToken).ConfigureAwait(false);
        return value is null ? Results.NotFound() : Results.Json(new CredentialReadResponse(value));
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError("CREDENTIAL_READ_FAILED provider={Provider} key={CredentialKey} errorType={ErrorType}",
            provider, key, exception.GetType().Name);
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Protected credential could not be read.");
    }
});

app.MapPut("/v1/credentials/{provider}/{key}", async (
    string provider,
    string key,
    CredentialWriteRequest request,
    ISecureCredentialStore store,
    CancellationToken cancellationToken) =>
{
    if (!CredentialNameValidator.IsValidProvider(provider) || !CredentialNameValidator.IsValidKey(key) ||
        string.IsNullOrWhiteSpace(request.Value) || request.Value.Length > 16_384)
        return Results.BadRequest(new { error = "invalid_credential_request" });
    try
    {
        await store.WriteAsync(provider, key, request.Value, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        app.Logger.LogError("CREDENTIAL_WRITE_FAILED provider={Provider} key={CredentialKey} errorType={ErrorType}",
            provider, key, exception.GetType().Name);
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Protected credential could not be stored.");
    }
});

app.MapDelete("/v1/credentials/{provider}/{key}", async (
    string provider,
    string key,
    ISecureCredentialStore store,
    CancellationToken cancellationToken) =>
{
    if (!CredentialNameValidator.IsValidProvider(provider) || !CredentialNameValidator.IsValidKey(key))
        return Results.BadRequest(new { error = "invalid_credential_name" });
    await store.DeleteAsync(provider, key, cancellationToken).ConfigureAwait(false);
    return Results.NoContent();
});

await app.RunAsync();

public sealed record CredentialReadResponse(string Value);
public sealed record CredentialWriteRequest(string Value);
internal partial class Program;
