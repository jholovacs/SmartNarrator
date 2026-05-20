using System.Net;
using System.Net.Sockets;

namespace SmartNarrator.Infrastructure.Ingest;

internal static class StoryUrlDownloadSafety
{
    public const int MaxRedirects = 8;

    public static async Task UriMustBeSafelyResolvableAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only http and https URLs are allowed.");

        var host = uri.IdnHost;
        ThrowIfDangerousHostnameLiteral(host);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"DNS resolution failed for host '{host}'.", ex);
        }

        if (addresses.Length == 0)
            throw new InvalidOperationException($"No addresses resolved for host '{host}'.");

        foreach (var a in addresses)
        {
            if (IsBlockedDestination(a))
                throw new InvalidOperationException(
                    $"Host '{host}' resolves to an address that is not allowed ({a}).");
        }
    }

    public static Uri ResolveRedirect(Uri current, Uri? locationHeader)
    {
        if (locationHeader is null)
            throw new InvalidOperationException("Redirect response was missing a Location header.");
        return locationHeader.IsAbsoluteUri ? locationHeader : new Uri(current, locationHeader);
    }

    internal static bool IsBlockedDestination(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        switch (ip.AddressFamily)
        {
            case AddressFamily.InterNetwork:
            {
                var b = ip.GetAddressBytes();
                return b.Length != 4 || IsIpv4Blocked(b.AsSpan());
            }
            case AddressFamily.InterNetworkV6:
            {
                if (IPAddress.IPv6Loopback.Equals(ip))
                    return true;
                var ipv6Bytes = ip.GetAddressBytes();
                if (ipv6Bytes.Length >= 16)
                {
                    if (ipv6Bytes[0] == 0xff) // multicast
                        return true;
                    // Unique local addresses FC00::/7
                    if (ipv6Bytes[0] is 0xfc or 0xfd)
                        return true;
                    if (ip.IsIPv6LinkLocal)
                        return true;
                    if (ip.IsIPv4MappedToIPv6)
                        return IsIpv4Blocked(ipv6Bytes.AsSpan(12, 4));
                }

                return false;
            }
            default:
                return true;
        }
    }

    private static void ThrowIfDangerousHostnameLiteral(string host)
    {
        var h = host.Trim('[', ']');
        if (string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(h, "127.0.0.1", StringComparison.Ordinal) ||
            string.Equals(h, "::1", StringComparison.Ordinal))
            throw new InvalidOperationException("localhost and loopback addresses are not allowed.");

        if (!IPAddress.TryParse(h, out var parsed))
            return;

        if (IsBlockedDestination(parsed))
            throw new InvalidOperationException("Loopback / private IP literals are not allowed.");
    }

    private static bool IsIpv4Blocked(ReadOnlySpan<byte> ipv4Bytes)
    {
        if (ipv4Bytes.Length != 4)
            return true;
        // 127.0.0.0/8
        if (ipv4Bytes[0] == 127)
            return true;
        // 10.0.0.0/8
        if (ipv4Bytes[0] == 10)
            return true;
        // 172.16.0.0/12
        if (ipv4Bytes[0] == 172 && ipv4Bytes[1] is >= 16 and <= 31)
            return true;
        // 192.168.0.0/16
        if (ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168)
            return true;
        // 169.254.0.0/16
        if (ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254)
            return true;
        // 100.64.0.0/10 CGNAT
        if (ipv4Bytes[0] == 100 && ipv4Bytes[1] is >= 64 and <= 127)
            return true;
        return false;
    }
}
