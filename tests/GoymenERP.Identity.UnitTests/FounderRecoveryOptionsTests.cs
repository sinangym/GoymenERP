using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

namespace GoymenERP.Identity.UnitTests;

/// <summary>KIM-D43 <c>Identity:FounderRecovery:TemporaryPasswordLifetime</c>: empty means 72 hours; invalid fails closed.</summary>
public sealed class FounderRecoveryOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_lifetime_means_the_decided_72_hours(string? configured)
    {
        var options = new FounderRecoveryOptions { TemporaryPasswordLifetime = configured };

        Assert.Equal(TimeSpan.FromHours(72), options.ResolveTemporaryPasswordLifetime());
        Assert.Equal("Identity:FounderRecovery", FounderRecoveryOptions.SectionName);
    }

    [Theory]
    [InlineData("1.00:00:00", 24 * 60)]
    [InlineData(" 12:30:00 ", 12 * 60 + 30)]
    public void A_positive_lifetime_is_used_as_configured(string configured, int expectedMinutes)
    {
        var options = new FounderRecoveryOptions { TemporaryPasswordLifetime = configured };

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), options.ResolveTemporaryPasswordLifetime());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00:00:00")]
    [InlineData("-01:00:00")]
    [InlineData("abc")]
    [InlineData("72h")]
    public void A_zero_negative_or_unreadable_lifetime_resolves_to_nothing(string configured)
    {
        var options = new FounderRecoveryOptions { TemporaryPasswordLifetime = configured };

        Assert.Null(options.ResolveTemporaryPasswordLifetime());
    }
}
