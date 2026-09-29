using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GfmStudioOS.SecureCredentialHelper;

public static class CredentialHelperRequestAuthorizer
{
    public static bool IsAllowed(IPAddress? address, IEnumerable<string> networks)
    {
        if (address is null)
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return networks.Select(IPNetwork.Parse).Any(network => network.Contains(address));
    }

    public static bool IsAuthorized(string? authorization, string expected)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var supplied = Encoding.UTF8.GetBytes(authorization[prefix.Length..]);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return supplied.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(supplied, expectedBytes);
    }
}
