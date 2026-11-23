namespace GoymenERP.BuildingBlocks.Security.Events;

/// <summary>
/// Counters of one <see cref="SecurityEventTypesV1.LoginRateLimited"/> summary (KIM-D29). It is the whole payload of
/// that event type: the summary window, how many attempts the limiter refused in it, and the limiter settings in
/// force (<see cref="PermitLimit"/> attempts per <see cref="Window"/>). It deliberately has no username, username
/// hash, IP address or partition key: the limiter partitions are never written to the security channel.
/// </summary>
/// <param name="WindowStartUtc">Start of the summary window (UTC, inclusive).</param>
/// <param name="WindowEndUtc">End of the summary window (UTC, exclusive); after <paramref name="WindowStartUtc"/>.</param>
/// <param name="RejectedAttemptCount">Refused (429) attempts in the window; a summary is written only while it is above zero.</param>
/// <param name="PermitLimit">The limiter's permitted attempts per <paramref name="Window"/>, as configured.</param>
/// <param name="Window">The limiter's window, as configured; whole seconds.</param>
public sealed record SecurityEventRateLimitSummaryV1(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int RejectedAttemptCount,
    int PermitLimit,
    TimeSpan Window)
{
    /// <summary>The limiter window in whole seconds, as persisted.</summary>
    public int WindowSeconds => (int)(Window.Ticks / TimeSpan.TicksPerSecond);
}
