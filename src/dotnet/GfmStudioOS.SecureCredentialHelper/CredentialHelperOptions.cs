using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GfmStudioOS.SecureCredentialHelper;

public sealed class CredentialHelperOptions
{
    public const string SectionName = "CredentialHelper";
    public int Port { get; set; } = 51823;
    public string SharedKey { get; set; } = string.Empty;
    public string[] AllowedClientNetworks { get; set; } = ["192.168.65.0/24"];
    public string? StorageDirectory { get; set; }
}

public sealed class CredentialHelperOptionsValidator : IValidateOptions<CredentialHelperOptions>
{
    public ValidateOptionsResult Validate(string? name, CredentialHelperOptions options)
    {
        var errors = new List<string>();
        if (options.Port is < 1024 or > 65535)
            errors.Add("CredentialHelper:Port must be between 1024 and 65535.");
        if (string.IsNullOrWhiteSpace(options.SharedKey) || EncodingLength(options.SharedKey) < 32)
            errors.Add("CredentialHelper:SharedKey must contain at least 32 UTF-8 bytes.");
        if (options.AllowedClientNetworks.Length == 0 ||
            options.AllowedClientNetworks.Any(network => !IPNetwork.TryParse(network, out _)))
            errors.Add("CredentialHelper:AllowedClientNetworks must contain valid CIDR networks.");
        if (options.StorageDirectory is { Length: > 0 } path && !Path.IsPathFullyQualified(path))
            errors.Add("CredentialHelper:StorageDirectory must be an absolute path.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static int EncodingLength(string value) => System.Text.Encoding.UTF8.GetByteCount(value);
}

public static partial class CredentialNameValidator
{
    public static bool IsValidProvider(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ProviderRegex().IsMatch(value);

    public static bool IsValidKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && KeyRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
}
