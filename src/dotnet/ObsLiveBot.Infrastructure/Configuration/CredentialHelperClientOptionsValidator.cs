using Microsoft.Extensions.Options;

namespace ObsLiveBot.Infrastructure.Configuration;

public sealed class CredentialHelperClientOptionsValidator
    : IValidateOptions<CredentialHelperClientOptions>
{
    public ValidateOptionsResult Validate(string? name, CredentialHelperClientOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttp ||
            baseUri.Port != 51823 ||
            baseUri.AbsolutePath != "/" ||
            baseUri.Host is not ("host.docker.internal" or "localhost" or "127.0.0.1"))
            return ValidateOptionsResult.Fail(
                "Credential helper URL must use the configured local host endpoint on port 51823.");

        if (options.TimeoutSeconds is < 1 or > 15)
            return ValidateOptionsResult.Fail("Credential helper timeout must be between 1 and 15 seconds.");

        if (!string.IsNullOrEmpty(options.SharedKey) &&
            System.Text.Encoding.UTF8.GetByteCount(options.SharedKey) < 32)
            return ValidateOptionsResult.Fail("Credential helper shared key must contain at least 32 UTF-8 bytes.");

        return ValidateOptionsResult.Success;
    }
}
