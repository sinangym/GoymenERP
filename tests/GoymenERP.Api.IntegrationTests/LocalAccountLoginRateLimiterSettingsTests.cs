using System.Net;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KIM-D34: the login rate limiter follows the group-wide Settings record. Nothing recorded keeps the configured starting value;
/// a recorded version replaces it on the next refresh; a read error or a value the host cannot apply never lifts the limit; the
/// refresh interval decides how often the record is read. The numbers are SENTETIK test inputs, not policy values.
/// </summary>
public sealed class LocalAccountLoginRateLimiterSettingsTests
{
    private const string Address = "192.0.2.10";

    [Fact]
    public async Task Without_a_recorded_version_the_configured_starting_value_stays_in_force()
    {
        using var limiter = Limiter(permitLimit: 2, refreshInterval: null, out _);
        var provider = new FakeProvider(_ => null);

        Assert.True(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.True(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.False(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.Null(limiter.Current.SettingsVersion);
        Assert.Equal(2, limiter.Current.PermitLimit);
    }

    [Fact]
    public async Task A_recorded_version_replaces_the_configured_value_on_the_next_attempt()
    {
        using var limiter = Limiter(permitLimit: 1, refreshInterval: null, out _);
        var provider = new FakeProvider(_ => Stored(version: 3, permitLimit: 3, LoginRateLimitPartitionsV1.IpAddress));

        Assert.True(await limiter.TryAcquireAsync(Request(Address), "a", provider, CancellationToken.None));
        Assert.True(await limiter.TryAcquireAsync(Request(Address), "b", provider, CancellationToken.None));
        Assert.True(await limiter.TryAcquireAsync(Request(Address), "c", provider, CancellationToken.None));
        Assert.False(await limiter.TryAcquireAsync(Request(Address), "d", provider, CancellationToken.None));
        Assert.Equal((3, TimeSpan.FromMinutes(1), LocalAccountLoginRateLimitPartition.IpAddress, (long?)3), limiter.Current);
    }

    [Fact]
    public async Task A_read_error_keeps_the_limit_in_force_and_never_lifts_it()
    {
        using var limiter = Limiter(permitLimit: 1, refreshInterval: null, out _);
        var provider = new FakeProvider(_ => throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable));

        Assert.True(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.False(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.Equal(1, limiter.Current.PermitLimit);
        Assert.Null(limiter.Current.SettingsVersion);
    }

    [Fact]
    public async Task A_stored_value_the_host_cannot_apply_keeps_the_limit_in_force()
    {
        using var limiter = Limiter(permitLimit: 1, refreshInterval: null, out _);
        var provider = new FakeProvider(_ => Stored(version: 4, permitLimit: 5, "ipaddress"));

        Assert.True(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.False(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.Null(limiter.Current.SettingsVersion);
    }

    [Fact]
    public async Task The_refresh_interval_decides_when_the_record_is_read_again()
    {
        using var limiter = Limiter(permitLimit: 1, refreshInterval: TimeSpan.FromSeconds(30), out var clock);
        var provider = new FakeProvider(_ => null);

        await limiter.TryAcquireAsync(Request("192.0.2.11"), "a", provider, CancellationToken.None);
        await limiter.TryAcquireAsync(Request("192.0.2.12"), "a", provider, CancellationToken.None);
        Assert.Equal(1, provider.Reads);

        clock.Advance(TimeSpan.FromSeconds(29));
        await limiter.TryAcquireAsync(Request("192.0.2.13"), "a", provider, CancellationToken.None);
        Assert.Equal(1, provider.Reads);

        clock.Advance(TimeSpan.FromSeconds(1));
        await limiter.TryAcquireAsync(Request("192.0.2.14"), "a", provider, CancellationToken.None);
        Assert.Equal(2, provider.Reads);
    }

    [Fact]
    public async Task The_same_limit_under_a_new_version_keeps_the_open_windows()
    {
        using var limiter = Limiter(permitLimit: 1, refreshInterval: null, out _);
        var version = 1L;
        var provider = new FakeProvider(_ => Stored(version, permitLimit: 1, LoginRateLimitPartitionsV1.IpAddressAndUserName));

        Assert.True(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        version = 2;
        Assert.False(await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, CancellationToken.None));
        Assert.Equal((long?)2, limiter.Current.SettingsVersion);
    }

    [Fact]
    public async Task A_cancelled_read_is_not_swallowed()
    {
        using var limiter = Limiter(permitLimit: 1, refreshInterval: null, out _);
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeProvider(token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return null;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await limiter.TryAcquireAsync(Request(Address), "kullanici", provider, cancellation.Token));
    }

    [Fact]
    public void A_non_positive_refresh_interval_is_refused_at_construction()
    {
        var options = Options(permitLimit: 1, refreshInterval: TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(() => new LocalAccountLoginRateLimiter(Microsoft.Extensions.Options.Options.Create(options)));
        Assert.Contains(options.Validate(), error => error.Contains("SettingsRefreshInterval", StringComparison.Ordinal));
    }

    private static SettingsLoginRateLimitV1 Stored(long version, int permitLimit, string partitionBy) =>
        new(new ProfileVersionV1(Guid.NewGuid(), version), permitLimit, TimeSpan.FromMinutes(1), partitionBy);

    private static LocalAccountOptions Options(int permitLimit, TimeSpan? refreshInterval)
    {
        var options = new LocalAccountOptions();
        options.RateLimit.Login.PermitLimit = permitLimit;
        options.RateLimit.Login.Window = TimeSpan.FromHours(1);
        options.RateLimit.Login.PartitionBy = LocalAccountLoginRateLimitPartition.IpAddressAndUserName;
        options.RateLimit.Login.SettingsRefreshInterval = refreshInterval;
        return options;
    }

    private static LocalAccountLoginRateLimiter Limiter(int permitLimit, TimeSpan? refreshInterval, out ManualClock clock)
    {
        clock = new ManualClock();
        return new LocalAccountLoginRateLimiter(
            Microsoft.Extensions.Options.Options.Create(Options(permitLimit, refreshInterval)),
            clock,
            NullLogger<LocalAccountLoginRateLimiter>.Instance);
    }

    private static DefaultHttpContext Request(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }

    private sealed class FakeProvider(Func<CancellationToken, SettingsLoginRateLimitV1?> answer) : ISettingsLoginRateLimitProviderV1
    {
        public int Reads { get; private set; }

        public Task<SettingsLoginRateLimitV1?> GetLoginRateLimitAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(answer(cancellationToken));
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan by) => ticks += by.Ticks;
    }
}
