using System.Net;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Api.Security;

/// <summary>
/// KIM-D42: the trusted proxy list the login rate limit uses to find the client address. <c>X-Forwarded-For</c> is read only
/// when the direct peer is on the list; an empty list (the default) means the header is never read. In a multi-hop header the
/// hops are walked from the right (the hop the direct peer added) and the first address that is not a trusted proxy is the
/// client; when every hop is trusted the leftmost one is. A hop that is not an address stops the walk at the nearest trusted
/// hop, so a malformed header never yields an address the trusted proxies did not see.
/// </summary>
internal sealed class LoginTrustedProxyList
{
    internal const string ForwardedForHeaderName = "X-Forwarded-For";

    private const int IPv4PrefixBits = 32;
    private const int IPv6PrefixBits = 128;
    private const char HopSeparator = ',';

    private readonly System.Net.IPNetwork[] networks;

    private LoginTrustedProxyList(System.Net.IPNetwork[] networks)
    {
        this.networks = networks;
    }

    /// <summary>The empty list: the forwarded header is never read.</summary>
    public static LoginTrustedProxyList Empty { get; } = new LoginTrustedProxyList([]);

    public bool IsEmpty => networks.Length == 0;

    /// <summary>
    /// The list built from canonical entries (an address or an address/prefix block); null when an entry cannot be read, so a
    /// value this host cannot apply is never half-applied.
    /// </summary>
    public static LoginTrustedProxyList? FromCanonical(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return Empty;
        }

        var parsed = new System.Net.IPNetwork[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                return null;
            }

            if (entry.Contains('/', StringComparison.Ordinal))
            {
                if (!System.Net.IPNetwork.TryParse(entry, out var network))
                {
                    return null;
                }

                parsed[index] = network;
            }
            else
            {
                if (!IPAddress.TryParse(entry, out var address))
                {
                    return null;
                }

                address = Normalize(address);
                parsed[index] = new System.Net.IPNetwork(address,
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? IPv4PrefixBits : IPv6PrefixBits);
            }
        }

        return new LoginTrustedProxyList(parsed);
    }

    /// <summary>Whether the address is a trusted proxy (an IPv4-mapped IPv6 address is compared as IPv4).</summary>
    public bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var normalized = Normalize(address);
        foreach (var network in networks)
        {
            if (network.Contains(normalized))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The client address of a request: the direct peer, unless the peer is a trusted proxy and the forwarded header names
    /// an earlier hop (see the class summary). Null when the connection has no peer address.
    /// </summary>
    public IPAddress? ResolveClientAddress(IPAddress? peer, StringValues forwardedFor)
    {
        if (peer is null)
        {
            return null;
        }

        var current = Normalize(peer);
        if (IsEmpty || !Contains(current))
        {
            return current;
        }

        var hops = new List<string>();
        foreach (var value in forwardedFor)
        {
            if (value is null)
            {
                continue;
            }

            foreach (var hop in value.Split(HopSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                hops.Add(hop);
            }
        }

        for (var index = hops.Count - 1; index >= 0; index--)
        {
            if (!IPAddress.TryParse(hops[index], out var hopAddress))
            {
                return current;
            }

            hopAddress = Normalize(hopAddress);
            if (!Contains(hopAddress))
            {
                return hopAddress;
            }

            current = hopAddress;
        }

        return current;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
