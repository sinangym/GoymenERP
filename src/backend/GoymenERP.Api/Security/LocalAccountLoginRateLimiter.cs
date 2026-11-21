using System.Threading.RateLimiting;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Security;

/// <summary>
/// The login rate limit (spec "Giriş hız sınırı", KIM-D29). It is separate from the lockout counter: it caps attempts per
/// window before any credential is read.
/// <list type="bullet">
/// <item><b>KIM-D34</b>: the value in force is the group-wide Settings record (<see cref="ISettingsLoginRateLimitProviderV1"/>,
/// Ayarlar → giriş deneme sınırı). <see cref="TryAcquireAsync"/> reads it when the copy it holds is older than
/// <c>Identity:LocalAccount:RateLimit:Login:SettingsRefreshInterval</c> (every attempt when not set); at most one read runs at a
/// time and the other attempts use the limit in force meanwhile.</item>
/// <item><b>Starting value / fallback</b>: <c>Identity:LocalAccount:RateLimit:Login</c> (there is no code default; the limiter is
/// built from validated settings and the mode does not start without them). It applies until the first Settings version is
/// recorded. When the record cannot be read, or holds a value this host cannot apply, the limit last applied stays in force
/// (a read error never lifts the limit) and a warning is logged.</item>
/// <item>A changed value replaces the limiter; the counters of the open windows start again from zero at that moment. A change
/// of the trusted proxy list alone keeps the open windows.</item>
/// <item><b>KIM-D42</b>: the client address a window counts is the direct peer, unless the peer is on the record's trusted proxy
/// list (<see cref="LoginTrustedProxyList"/>); then <c>X-Forwarded-For</c> names it. The configured starting value has an empty
/// list, so until a Settings version names a proxy the header is never read.</item>
/// </list>
/// </summary>
internal sealed class LocalAccountLoginRateLimiter : IDisposable
{
    private const char ProxySeparator = ',';

    private readonly LimitValues configured;
    private readonly TimeSpan? refreshInterval;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);
    private volatile LimiterState state;
    private long lastRefreshTimestamp;
    private int hasRefreshed;
    private int disposed;

    public LocalAccountLoginRateLimiter(IOptions<LocalAccountOptions> options)
        : this(options, TimeProvider.System, NullLogger<LocalAccountLoginRateLimiter>.Instance)
    {
    }

    public LocalAccountLoginRateLimiter(
        IOptions<LocalAccountOptions> options,
        TimeProvider timeProvider,
        ILogger<LocalAccountLoginRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        var settings = options.Value.RateLimit.Login;
        if (settings.PermitLimit is not >= 1
            || settings.Window is not { } window
            || window <= TimeSpan.Zero
            || settings.PartitionBy is not { } partitionBy
            || !Enum.IsDefined(partitionBy)
            || (settings.SettingsRefreshInterval is { } interval && interval <= TimeSpan.Zero))
        {
            throw new InvalidOperationException(
                "Yerel hesap giriş hız sınırı ayarları eksik veya geçersiz (Identity:LocalAccount:RateLimit:Login).");
        }

        configured = new LimitValues(settings.PermitLimit.Value, window, partitionBy, TrustedProxies: string.Empty,
            SettingsVersion: null);
        refreshInterval = settings.SettingsRefreshInterval;
        this.timeProvider = timeProvider;
        this.logger = logger;
        state = Build(configured, LoginTrustedProxyList.Empty);
    }

    /// <summary>The limit in force (for diagnostics and tests): attempts, window, partition and the Settings version.</summary>
    internal (int PermitLimit, TimeSpan Window, LocalAccountLoginRateLimitPartition PartitionBy, long? SettingsVersion) Current
    {
        get
        {
            var values = state.Values;
            return (values.PermitLimit, values.Window, values.PartitionBy, values.SettingsVersion);
        }
    }

    /// <summary>
    /// Brings the limit up to the group-wide Settings record when the copy held is stale (KIM-D34), then takes one attempt
    /// from the caller's window; false means the window is used up (HTTP 429).
    /// </summary>
    public async ValueTask<bool> TryAcquireAsync(
        HttpContext httpContext,
        string? userName,
        ISettingsLoginRateLimitProviderV1 provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(provider);
        await RefreshIfStaleAsync(provider, cancellationToken);
        return TryAcquire(httpContext, userName);
    }

    /// <summary>Takes one attempt from the caller's window with the limit in force; false means the window is used up.</summary>
    public bool TryAcquire(HttpContext httpContext, string? userName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);
        var current = state;
        try
        {
            return Attempt(current, httpContext, userName);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) == 0 && !ReferenceEquals(current, state))
        {
            // A refresh replaced the limiter between the read of the state and the attempt; the new one counts it.
            return Attempt(state, httpContext, userName);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        // The refresh gate is not disposed: a refresh may still release it, and a SemaphoreSlim without a wait handle holds
        // nothing to free.
        state.Limiter.Dispose();
    }

    private async Task RefreshIfStaleAsync(ISettingsLoginRateLimitProviderV1 provider, CancellationToken cancellationToken)
    {
        if (!IsStale() || Volatile.Read(ref disposed) == 1)
        {
            return;
        }

        if (!await refreshGate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            // Another attempt is reading the record now; this one uses the limit in force.
            return;
        }

        try
        {
            if (!IsStale() || Volatile.Read(ref disposed) == 1)
            {
                return;
            }

            SettingsLoginRateLimitV1? stored;
            try
            {
                stored = await provider.GetLoginRateLimitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MarkRefreshed();
                logger.LogWarning(exception,
                    "Login rate limit Settings record could not be read; the limit in force stays. SettingsVersion={SettingsVersion}",
                    state.Values.SettingsVersion);
                return;
            }

            MarkRefreshed();
            var target = stored is null ? configured : FromSettings(stored);
            var proxies = target is null ? null : LoginTrustedProxyList.FromCanonical(SplitProxies(target.TrustedProxies));
            if (target is null || proxies is null)
            {
                logger.LogWarning(
                    "Login rate limit Settings record holds a value this host cannot apply; the limit in force stays. StoredVersion={StoredVersion}",
                    stored!.Profile.Version);
                return;
            }

            var previous = state;
            if (previous.Values == target)
            {
                return;
            }

            if (previous.Values with { SettingsVersion = target.SettingsVersion, TrustedProxies = target.TrustedProxies } == target)
            {
                // The same limit under another version or proxy list (or back to the configured value): keep the open windows.
                state = previous with { Values = target, Proxies = proxies };
                return;
            }

            state = Build(target, proxies);
            previous.Limiter.Dispose();
            logger.LogInformation(
                "Login rate limit changed. PermitLimit={PermitLimit} Window={Window} PartitionBy={PartitionBy} SettingsVersion={SettingsVersion}",
                target.PermitLimit, target.Window, target.PartitionBy, target.SettingsVersion);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private bool IsStale() =>
        Volatile.Read(ref hasRefreshed) == 0
        || refreshInterval is not { } interval
        || timeProvider.GetElapsedTime(Interlocked.Read(ref lastRefreshTimestamp)) >= interval;

    private void MarkRefreshed()
    {
        Interlocked.Exchange(ref lastRefreshTimestamp, timeProvider.GetTimestamp());
        Volatile.Write(ref hasRefreshed, 1);
    }

    /// <summary>
    /// The stored version as this host's values, or null when it cannot be applied (the partition name must be one of the
    /// configuration's partition names exactly; a numeric or unknown text is refused).
    /// </summary>
    private static LimitValues? FromSettings(SettingsLoginRateLimitV1 stored)
    {
        if (stored.PermitLimit < 1 || stored.Window <= TimeSpan.Zero
            || !Enum.GetNames<LocalAccountLoginRateLimitPartition>().Contains(stored.PartitionBy, StringComparer.Ordinal))
        {
            return null;
        }

        return new LimitValues(
            stored.PermitLimit,
            stored.Window,
            Enum.Parse<LocalAccountLoginRateLimitPartition>(stored.PartitionBy, ignoreCase: false),
            string.Join(ProxySeparator, stored.TrustedProxies ?? []),
            stored.Profile.Version);
    }

    private static IReadOnlyList<string> SplitProxies(string trustedProxies) =>
        trustedProxies.Length == 0 ? [] : trustedProxies.Split(ProxySeparator);

    private static LimiterState Build(LimitValues values, LoginTrustedProxyList proxies)
    {
        var permitLimit = values.PermitLimit;
        var window = values.Window;
        var limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
        return new LimiterState(limiter, values, proxies);
    }

    private static bool Attempt(LimiterState current, HttpContext httpContext, string? userName)
    {
        using var lease = current.Limiter.AttemptAcquire(
            PartitionKey(current.Values.PartitionBy, current.Proxies, httpContext, userName));
        return lease.IsAcquired;
    }

    private static string PartitionKey(
        LocalAccountLoginRateLimitPartition partition,
        LoginTrustedProxyList proxies,
        HttpContext httpContext,
        string? userName)
    {
        // KIM-D42: the direct peer, or — only when the peer is a trusted proxy — the client the forwarded header names.
        var address = ClientAddress(proxies, httpContext)?.ToString() ?? string.Empty;
        if (partition == LocalAccountLoginRateLimitPartition.IpAddress)
        {
            return address;
        }

        // The normalized name keeps "ali", " ALI " and "Ali" in one window; an unusable name shares one bucket.
        return string.Concat(address, "\u001f", LocalUserName.TryNormalize(userName) ?? string.Empty);
    }

    /// <summary>
    /// The client address the login rate limit counts (KIM-D42): the forwarded header is not even looked up while the list is
    /// empty or the peer is not on it.
    /// </summary>
    internal static System.Net.IPAddress? ClientAddress(LoginTrustedProxyList proxies, HttpContext httpContext)
    {
        var peer = httpContext.Connection.RemoteIpAddress;
        if (peer is null || proxies.IsEmpty || !proxies.Contains(peer))
        {
            return peer;
        }

        return proxies.ResolveClientAddress(peer, httpContext.Request.Headers[LoginTrustedProxyList.ForwardedForHeaderName]);
    }

    /// <summary>
    /// The values a limiter is built from; <c>SettingsVersion</c> is null for the configured starting value and
    /// <c>TrustedProxies</c> is the canonical list text (empty: the forwarded header is never read).
    /// </summary>
    private sealed record LimitValues(
        int PermitLimit,
        TimeSpan Window,
        LocalAccountLoginRateLimitPartition PartitionBy,
        string TrustedProxies,
        long? SettingsVersion);

    private sealed record LimiterState(PartitionedRateLimiter<string> Limiter, LimitValues Values, LoginTrustedProxyList Proxies);
}
