using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// KIM-D34: the group-wide login attempt limit's input rules only check the technical shape — a count and a window inside the
/// published bounds and a partition name spelled exactly. The numbers below are rule inputs, not policy values.
/// </summary>
public sealed class LoginRateLimitRulesV1Tests
{
    [Fact]
    public void A_permit_limit_is_accepted_only_inside_the_published_bounds()
    {
        Assert.True(LoginRateLimitRulesV1.IsPermitLimit(LoginRateLimitInputLimitsV1.MinimumPermitLimit));
        Assert.True(LoginRateLimitRulesV1.IsPermitLimit(LoginRateLimitInputLimitsV1.MaximumPermitLimit));
        Assert.False(LoginRateLimitRulesV1.IsPermitLimit(null));
        Assert.False(LoginRateLimitRulesV1.IsPermitLimit(LoginRateLimitInputLimitsV1.MinimumPermitLimit - 1));
        Assert.False(LoginRateLimitRulesV1.IsPermitLimit(LoginRateLimitInputLimitsV1.MaximumPermitLimit + 1));
        Assert.False(LoginRateLimitRulesV1.IsPermitLimit(-1));
    }

    [Fact]
    public void A_window_is_accepted_only_inside_the_published_bounds()
    {
        Assert.True(LoginRateLimitRulesV1.IsWindowSeconds(LoginRateLimitInputLimitsV1.MinimumWindowSeconds));
        Assert.True(LoginRateLimitRulesV1.IsWindowSeconds(LoginRateLimitInputLimitsV1.MaximumWindowSeconds));
        Assert.False(LoginRateLimitRulesV1.IsWindowSeconds(null));
        Assert.False(LoginRateLimitRulesV1.IsWindowSeconds(LoginRateLimitInputLimitsV1.MinimumWindowSeconds - 1));
        Assert.False(LoginRateLimitRulesV1.IsWindowSeconds(LoginRateLimitInputLimitsV1.MaximumWindowSeconds + 1));
    }

    [Theory]
    [InlineData("IpAddress")]
    [InlineData("IpAddressAndUserName")]
    public void A_published_partition_name_spelled_exactly_is_accepted(string value)
    {
        Assert.True(LoginRateLimitRulesV1.IsPartition(value));
        Assert.Contains(value, LoginRateLimitPartitionsV1.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ipaddress")]
    [InlineData(" IpAddress")]
    [InlineData("IpAddress ")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("UserName")]
    public void Any_other_partition_text_is_refused(string? value)
    {
        Assert.False(LoginRateLimitRulesV1.IsPartition(value));
    }

    [Fact]
    public void The_published_partition_list_is_closed_and_without_duplicates()
    {
        Assert.Equal(["IpAddress", "IpAddressAndUserName"], LoginRateLimitPartitionsV1.All);
        Assert.Equal(LoginRateLimitPartitionsV1.All.Count, LoginRateLimitPartitionsV1.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("198.51.100.0/24", "198.51.100.0/24")]
    [InlineData("0.0.0.0/0", "0.0.0.0/0")]
    [InlineData("2001:DB8::1", "2001:db8::1")]
    [InlineData("2001:db8:0:0::/64", "2001:db8::/64")]
    public void A_trusted_proxy_address_or_block_is_accepted_in_its_canonical_form(string value, string expected)
    {
        Assert.True(LoginRateLimitRulesV1.TryNormalizeTrustedProxy(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 192.0.2.10")]
    [InlineData("192.0.2.10 ")]
    [InlineData("192.0.2")]
    [InlineData("192.0.2.010")]
    [InlineData("192.0.2.256")]
    [InlineData("192.0.2.10/24")]
    [InlineData("192.0.2.0/33")]
    [InlineData("192.0.2.0/024")]
    [InlineData("192.0.2.0/")]
    [InlineData("2001:db8::1/64")]
    [InlineData("fe80::1%1")]
    [InlineData("::ffff:192.0.2.10")]
    [InlineData("proxy.example")]
    public void A_malformed_trusted_proxy_entry_is_refused(string value)
    {
        Assert.False(LoginRateLimitRulesV1.TryNormalizeTrustedProxy(value, out _));
    }

    [Fact]
    public void A_trusted_proxy_list_is_canonical_distinct_and_ordered_and_an_empty_list_is_valid()
    {
        Assert.True(LoginRateLimitRulesV1.TryNormalizeTrustedProxies([], out var empty));
        Assert.Empty(empty);
        Assert.Equal(string.Empty, LoginRateLimitRulesV1.JoinTrustedProxies(empty));

        Assert.True(LoginRateLimitRulesV1.TryNormalizeTrustedProxies(
            ["198.51.100.0/24", "192.0.2.10", "192.0.2.10", "2001:DB8::1"], out var list));
        Assert.Equal(["192.0.2.10", "198.51.100.0/24", "2001:db8::1"], list);
        Assert.Equal("192.0.2.10,198.51.100.0/24,2001:db8::1", LoginRateLimitRulesV1.JoinTrustedProxies(list));
    }

    [Fact]
    public void A_missing_or_oversized_trusted_proxy_list_is_refused()
    {
        Assert.False(LoginRateLimitRulesV1.TryNormalizeTrustedProxies(null, out _));
        Assert.False(LoginRateLimitRulesV1.TryNormalizeTrustedProxies(["192.0.2.10", "not-an-address"], out _));
        var tooMany = Enumerable.Range(0, LoginRateLimitInputLimitsV1.MaximumTrustedProxyCount + 1)
            .Select(index => $"10.0.{index / 256}.{index % 256}")
            .ToArray();
        Assert.False(LoginRateLimitRulesV1.TryNormalizeTrustedProxies(tooMany, out _));
        Assert.True(LoginRateLimitRulesV1.TryNormalizeTrustedProxies(
            tooMany.Take(LoginRateLimitInputLimitsV1.MaximumTrustedProxyCount).ToArray(), out _));
    }

    [Fact]
    public void A_stored_trusted_proxy_list_is_read_only_in_its_canonical_form()
    {
        Assert.True(LoginRateLimitRulesV1.TryReadTrustedProxies(string.Empty, out var empty));
        Assert.Empty(empty);
        Assert.True(LoginRateLimitRulesV1.TryReadTrustedProxies("192.0.2.10,198.51.100.0/24", out var list));
        Assert.Equal(["192.0.2.10", "198.51.100.0/24"], list);

        Assert.False(LoginRateLimitRulesV1.TryReadTrustedProxies(null, out _));
        Assert.False(LoginRateLimitRulesV1.TryReadTrustedProxies("198.51.100.0/24,192.0.2.10", out _));
        Assert.False(LoginRateLimitRulesV1.TryReadTrustedProxies("192.0.2.10,192.0.2.10", out _));
        Assert.False(LoginRateLimitRulesV1.TryReadTrustedProxies("2001:DB8::1", out _));
        Assert.False(LoginRateLimitRulesV1.TryReadTrustedProxies("192.0.2.10,", out _));
    }
}
