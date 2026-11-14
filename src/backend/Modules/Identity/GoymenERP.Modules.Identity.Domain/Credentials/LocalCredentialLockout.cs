namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// Pure lockout rules of the local account (spec "Kilit sayacı"; KIM-D4 threshold, KIM-D13 duration). The
/// threshold and the duration are always configuration; nothing here holds a policy value.
/// </summary>
public static class LocalCredentialLockout
{
    /// <summary>An account is locked while its lockout end lies strictly in the future.</summary>
    public static bool IsLockedOut(DateTimeOffset? lockoutEndUtc, DateTimeOffset nowUtc) =>
        lockoutEndUtc is { } end && end > nowUtc;

    /// <summary>
    /// The state after one more wrong password on an account that is not locked. Reaching the threshold sets the
    /// lockout end and restarts the counter at zero, so the count starts from zero when the lock expires.
    /// </summary>
    public static LocalCredentialFailureTransition AfterFailedAccess(
        int failedAccessCount,
        DateTimeOffset? lockoutEndUtc,
        LocalCredentialLockoutPolicy policy,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (failedAccessCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAccessCount));
        }

        var next = failedAccessCount + 1;
        return next >= policy.MaxFailedAttempts
            ? new LocalCredentialFailureTransition(0, nowUtc + policy.Duration, LockedNow: true)
            : new LocalCredentialFailureTransition(next, lockoutEndUtc, LockedNow: false);
    }
}

/// <summary>The configured lockout policy (values from <c>Identity:LocalAccount:Lockout</c>).</summary>
public sealed record LocalCredentialLockoutPolicy
{
    public LocalCredentialLockoutPolicy(int maxFailedAttempts, TimeSpan duration)
    {
        if (maxFailedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFailedAttempts), "Kilit eşiği en az 1 olmalıdır.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Kilit süresi pozitif olmalıdır.");
        }

        MaxFailedAttempts = maxFailedAttempts;
        Duration = duration;
    }

    public int MaxFailedAttempts { get; }

    public TimeSpan Duration { get; }
}

/// <summary>Counter and lockout end after a failed attempt.</summary>
public sealed record LocalCredentialFailureTransition(int FailedAccessCount, DateTimeOffset? LockoutEndUtc, bool LockedNow);
