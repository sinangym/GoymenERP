using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// The <c>Identity:LocalAccount</c> settings (spec "Birim/formül/yuvarlama/tolerans"). No value has a code
/// default: every value comes from configuration and is validated at startup only when the <c>LocalAccount</c>
/// mode is on. A missing or invalid value stops the host (fail-closed).
/// </summary>
public sealed class LocalAccountOptions
{
    public const string SectionName = "Identity:LocalAccount";

    /// <summary>
    /// The lowest PBKDF2 iteration count accepted: the framework <c>PasswordHasher</c> V3 default. A configured
    /// value may only raise the cost, never lower it below what the framework ships with.
    /// </summary>
    public const int MinimumPbkdf2Iterations = 100_000;

    public LocalAccountLockoutSettings Lockout { get; set; } = new();

    public LocalAccountSessionSettings Session { get; set; } = new();

    public LocalAccountPasswordHashingSettings PasswordHashing { get; set; } = new();

    public LocalAccountRateLimitSettings RateLimit { get; set; } = new();

    /// <summary>Returns Turkish startup messages; an empty list means the settings are complete and valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Lockout.MaxFailedAttempts is not >= 1)
        {
            errors.Add($"{SectionName}:Lockout:MaxFailedAttempts en az 1 olmalıdır (KIM-D4).");
        }

        if (Lockout.Duration is not { } lockoutDuration || lockoutDuration <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:Lockout:Duration pozitif bir süre olmalıdır (KIM-D13).");
        }

        if (Session.IdleTimeout is not { } idle || idle <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:Session:IdleTimeout pozitif bir süre olmalıdır (KIM-D4).");
        }

        if (Session.AbsoluteLifetime is not { } absolute || absolute <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:Session:AbsoluteLifetime pozitif bir süre olmalıdır (KIM-D14).");
        }
        else if (Session.IdleTimeout is { } idleTimeout && idleTimeout > absolute)
        {
            errors.Add($"{SectionName}:Session:IdleTimeout mutlak oturum süresinden uzun olamaz.");
        }

        if (!string.Equals(PasswordHashing.Algorithm, LocalAccountPasswordHashingSettings.Pbkdf2Algorithm, StringComparison.Ordinal))
        {
            errors.Add($"{SectionName}:PasswordHashing:Algorithm yalnız '{LocalAccountPasswordHashingSettings.Pbkdf2Algorithm}' olabilir (KIM-D12).");
        }

        if (PasswordHashing.Pbkdf2Iterations is not { } iterations || iterations < MinimumPbkdf2Iterations)
        {
            errors.Add($"{SectionName}:PasswordHashing:Pbkdf2Iterations en az {MinimumPbkdf2Iterations} olmalıdır.");
        }

        var login = RateLimit.Login;
        if (login.PermitLimit is not >= 1)
        {
            errors.Add($"{SectionName}:RateLimit:Login:PermitLimit tanımlanmalı ve en az 1 olmalıdır (ASSUMPTION-ID-RL-1 kararı bekleniyor).");
        }

        if (login.Window is not { } window || window <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:RateLimit:Login:Window pozitif bir süre olmalıdır (ASSUMPTION-ID-RL-1 kararı bekleniyor).");
        }

        if (login.PartitionBy is not { } partition || !Enum.IsDefined(partition))
        {
            errors.Add($"{SectionName}:RateLimit:Login:PartitionBy tanımlanmalıdır (ASSUMPTION-ID-RL-1 kararı bekleniyor).");
        }

        return errors;
    }

    /// <summary>Throws with every Turkish message when the settings are incomplete or invalid.</summary>
    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Yerel hesap (LocalAccount) ayarları eksik veya geçersiz: " + string.Join(" ", errors));
        }
    }

    /// <summary>The validated lockout policy; call only after <see cref="EnsureValid"/>.</summary>
    public LocalCredentialLockoutPolicy ToLockoutPolicy() =>
        new(Lockout.MaxFailedAttempts!.Value, Lockout.Duration!.Value);
}

public sealed class LocalAccountLockoutSettings
{
    public string? PolicyRef { get; set; }

    public int? MaxFailedAttempts { get; set; }

    public TimeSpan? Duration { get; set; }
}

public sealed class LocalAccountSessionSettings
{
    public string? PolicyRef { get; set; }

    public TimeSpan? IdleTimeout { get; set; }

    public TimeSpan? AbsoluteLifetime { get; set; }
}

public sealed class LocalAccountPasswordHashingSettings
{
    public const string Pbkdf2Algorithm = "Pbkdf2";

    public string? PolicyRef { get; set; }

    public string? Algorithm { get; set; }

    public int? Pbkdf2Iterations { get; set; }
}

public sealed class LocalAccountRateLimitSettings
{
    public LocalAccountLoginRateLimitSettings Login { get; set; } = new();
}

/// <summary>Login rate limit (ASSUMPTION-ID-RL-1 candidate). It is separate from the lockout counter.</summary>
public sealed class LocalAccountLoginRateLimitSettings
{
    public string? PolicyRef { get; set; }

    public int? PermitLimit { get; set; }

    public TimeSpan? Window { get; set; }

    public LocalAccountLoginRateLimitPartition? PartitionBy { get; set; }
}

public enum LocalAccountLoginRateLimitPartition
{
    /// <summary>One window per client IP address.</summary>
    IpAddress = 1,

    /// <summary>One window per client IP address and normalized user name (spec proposal).</summary>
    IpAddressAndUserName = 2,
}
