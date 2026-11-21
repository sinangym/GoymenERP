using System.Globalization;
using System.Net;
using System.Net.Sockets;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The pure input rules of the group-wide login attempt limit (KIM-D34) and its trusted proxy list (KIM-D42). They only check
/// the technical shape of a value; which limit and which proxies the group uses is a Settings value, never chosen here.
/// </summary>
public static class LoginRateLimitRulesV1
{
    /// <summary>The separator of the stored canonical trusted proxy list.</summary>
    public const char TrustedProxySeparator = ',';

    /// <summary>The longest accepted entry: the longest IPv6 text form (45 characters) and a "/128" prefix.</summary>
    private const int TrustedProxyEntryMaximumLength = 49;

    private const int IPv4PrefixBits = 32;
    private const int IPv6PrefixBits = 128;
    private const int IPv4PartCount = 4;
    private const int IPv4PartMaximumDigits = 3;
    private const int IPv4PartMaximumValue = 255;
    private const int PrefixMaximumDigits = 3;
    private const int BitsPerByte = 8;

    /// <summary>An attempt count inside the technical bounds.</summary>
    public static bool IsPermitLimit(int? value) =>
        value is >= LoginRateLimitInputLimitsV1.MinimumPermitLimit and <= LoginRateLimitInputLimitsV1.MaximumPermitLimit;

    /// <summary>A window length in seconds inside the technical bounds.</summary>
    public static bool IsWindowSeconds(int? value) =>
        value is >= LoginRateLimitInputLimitsV1.MinimumWindowSeconds and <= LoginRateLimitInputLimitsV1.MaximumWindowSeconds;

    /// <summary>One of the published partition names, spelled exactly (no case folding, no trimming).</summary>
    public static bool IsPartition(string? value) =>
        value is not null && LoginRateLimitPartitionsV1.All.Contains(value, StringComparer.Ordinal);

    /// <summary>
    /// KIM-D42: the canonical form of a requested trusted proxy list — every entry an address or a CIDR block (see
    /// <see cref="TryNormalizeTrustedProxy"/>), duplicates folded, ordinal order — or false for a missing list, a malformed
    /// entry, more than <see cref="LoginRateLimitInputLimitsV1.MaximumTrustedProxyCount"/> entries or a canonical text longer
    /// than <see cref="LoginRateLimitInputLimitsV1.TrustedProxiesStorageMaximumLength"/>. An empty list is valid (the header
    /// is then never read).
    /// </summary>
    public static bool TryNormalizeTrustedProxies(IReadOnlyList<string>? values, out IReadOnlyList<string> normalized)
    {
        normalized = [];
        if (values is null || values.Count > LoginRateLimitInputLimitsV1.MaximumTrustedProxyCount)
        {
            return false;
        }

        var entries = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!TryNormalizeTrustedProxy(value, out var entry))
            {
                return false;
            }

            entries.Add(entry);
        }

        if (JoinTrustedProxies([.. entries]).Length > LoginRateLimitInputLimitsV1.TrustedProxiesStorageMaximumLength)
        {
            return false;
        }

        normalized = [.. entries];
        return true;
    }

    /// <summary>
    /// KIM-D42: the canonical form of one trusted proxy entry. Accepted: an IPv4 address in dotted-quad form (no leading
    /// zeros), an IPv6 address (no zone, not IPv4-mapped), or either followed by <c>/prefix</c> with no host bits set past
    /// the prefix. No surrounding spaces. The canonical form is the address's own text (IPv6 compressed, lower case) and,
    /// for a block, <c>/prefix</c> in decimal.
    /// </summary>
    public static bool TryNormalizeTrustedProxy(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length > TrustedProxyEntryMaximumLength)
        {
            return false;
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        var addressText = slash < 0 ? value : value[..slash];
        if (!TryParseAddress(addressText, out var address))
        {
            return false;
        }

        if (slash < 0)
        {
            normalized = address.ToString();
            return true;
        }

        var prefixText = value[(slash + 1)..];
        if (prefixText.Length is 0 or > PrefixMaximumDigits
            || !prefixText.All(char.IsAsciiDigit)
            || (prefixText.Length > 1 && prefixText[0] == '0'))
        {
            return false;
        }

        var prefix = int.Parse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture);
        var bits = address.AddressFamily == AddressFamily.InterNetwork ? IPv4PrefixBits : IPv6PrefixBits;
        if (prefix > bits || !HostBitsAreZero(address.GetAddressBytes(), prefix))
        {
            return false;
        }

        normalized = string.Concat(address.ToString(), "/", prefix.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>The stored text of a canonical trusted proxy list (empty text for an empty list).</summary>
    public static string JoinTrustedProxies(IReadOnlyList<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return string.Join(TrustedProxySeparator, entries);
    }

    /// <summary>
    /// KIM-D42: reads a stored trusted proxy list; false when the text is missing or not canonical (an entry not in canonical
    /// form, a duplicate, an order other than ordinal or too many entries) — malformed stored data.
    /// </summary>
    public static bool TryReadTrustedProxies(string? stored, out IReadOnlyList<string> entries)
    {
        entries = [];
        if (stored is null)
        {
            return false;
        }

        if (stored.Length == 0)
        {
            return true;
        }

        var parts = stored.Split(TrustedProxySeparator);
        if (parts.Length > LoginRateLimitInputLimitsV1.MaximumTrustedProxyCount)
        {
            return false;
        }

        for (var index = 0; index < parts.Length; index++)
        {
            if (!TryNormalizeTrustedProxy(parts[index], out var canonical)
                || !string.Equals(canonical, parts[index], StringComparison.Ordinal)
                || (index > 0 && string.CompareOrdinal(parts[index - 1], parts[index]) >= 0))
            {
                return false;
            }
        }

        entries = parts;
        return true;
    }

    private static bool TryParseAddress(string text, out IPAddress address)
    {
        address = IPAddress.None;
        if (text.Contains(':', StringComparison.Ordinal))
        {
            if (text.Contains('%', StringComparison.Ordinal)
                || !IPAddress.TryParse(text, out var parsed)
                || parsed.AddressFamily != AddressFamily.InterNetworkV6
                || parsed.ScopeId != 0
                || parsed.IsIPv4MappedToIPv6)
            {
                return false;
            }

            address = parsed;
            return true;
        }

        var parts = text.Split('.');
        if (parts.Length != IPv4PartCount)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is 0 or > IPv4PartMaximumDigits
                || !part.All(char.IsAsciiDigit)
                || (part.Length > 1 && part[0] == '0')
                || int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture) > IPv4PartMaximumValue)
            {
                return false;
            }
        }

        address = IPAddress.Parse(text);
        return true;
    }

    private static bool HostBitsAreZero(byte[] bytes, int prefix)
    {
        for (var bit = prefix; bit < bytes.Length * BitsPerByte; bit++)
        {
            if ((bytes[bit / BitsPerByte] & (0x80 >> (bit % BitsPerByte))) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
