using System.Threading.RateLimiting;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Security;

/// <summary>
/// The login rate limit (spec "Giriş hız sınırı", <c>Identity:LocalAccount:RateLimit:Login</c>). It is separate from
/// the lockout counter: it caps attempts per window before any credential is read. There is no code default; the
/// limiter is built from validated settings and the mode does not start without them (KIM-D29).
/// </summary>
internal sealed class LocalAccountLoginRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;
    private readonly LocalAccountLoginRateLimitPartition partition;

    public LocalAccountLoginRateLimiter(IOptions<LocalAccountOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value.RateLimit.Login;
        if (settings.PermitLimit is not >= 1
            || settings.Window is not { } window
            || window <= TimeSpan.Zero
            || settings.PartitionBy is not { } partitionBy
            || !Enum.IsDefined(partitionBy))
        {
            throw new InvalidOperationException(
                "Yerel hesap giriş hız sınırı ayarları eksik veya geçersiz (Identity:LocalAccount:RateLimit:Login).");
        }

        partition = partitionBy;
        var permitLimit = settings.PermitLimit.Value;
        limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    /// <summary>Takes one attempt from the caller's window; false means the window is used up (HTTP 429).</summary>
    public bool TryAcquire(HttpContext httpContext, string? userName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        using var lease = limiter.AttemptAcquire(PartitionKey(httpContext, userName));
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();

    private string PartitionKey(HttpContext httpContext, string? userName)
    {
        // The direct peer address; behind a proxy the forwarded-header setup decides what this is (NEEDS-SINAN).
        var address = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        if (partition == LocalAccountLoginRateLimitPartition.IpAddress)
        {
            return address;
        }

        // The normalized name keeps "ali", " ALI " and "Ali" in one window; an unusable name shares one bucket.
        return string.Concat(address, "\u001f", LocalUserName.TryNormalize(userName) ?? string.Empty);
    }
}
