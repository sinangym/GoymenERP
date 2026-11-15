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
}
