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

    /// <summary>
    /// Spec (NIST SP 800-63B): the accepted maximum password length must be at least 64. A configured
    /// <c>PasswordPolicy:MaximumLength</c> below this is refused at startup.
    /// </summary>
    public const int MinimumAcceptedMaximumPasswordLength = 64;

    public LocalAccountLockoutSettings Lockout { get; set; } = new();

    public LocalAccountSessionSettings Session { get; set; } = new();

    public LocalAccountPasswordHashingSettings PasswordHashing { get; set; } = new();

    public LocalAccountRateLimitSettings RateLimit { get; set; } = new();

    public LocalAccountPasswordPolicySettings PasswordPolicy { get; set; } = new();

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
            errors.Add($"{SectionName}:RateLimit:Login:PermitLimit tanımlanmalı ve en az 1 olmalıdır (KIM-D29).");
        }

        if (login.Window is not { } window || window <= TimeSpan.Zero)
        {
            errors.Add($"{SectionName}:RateLimit:Login:Window pozitif bir süre olmalıdır (KIM-D29).");
        }

        if (login.PartitionBy is not { } partition || !Enum.IsDefined(partition))
        {
            errors.Add($"{SectionName}:RateLimit:Login:PartitionBy tanımlanmalıdır (KIM-D29).");
        }

        ValidatePasswordPolicy(errors);
        return errors;
    }

    private void ValidatePasswordPolicy(List<string> errors)
    {
        var policy = PasswordPolicy;
        if (policy.MinimumLength is not >= 1)
        {
            errors.Add($"{SectionName}:PasswordPolicy:MinimumLength tanımlanmalı ve en az 1 olmalıdır (KIM-D4).");
        }

        if (policy.MaximumLength is not { } maximum || maximum < MinimumAcceptedMaximumPasswordLength)
        {
            errors.Add($"{SectionName}:PasswordPolicy:MaximumLength tanımlanmalı ve en az {MinimumAcceptedMaximumPasswordLength} olmalıdır (NIST SP 800-63B).");
        }
        else if (policy.MinimumLength is { } minimum && minimum > maximum)
        {
            errors.Add($"{SectionName}:PasswordPolicy:MinimumLength azami uzunluktan büyük olamaz.");
        }

        if (policy.BlocklistEnabled is null)
        {
            errors.Add($"{SectionName}:PasswordPolicy:BlocklistEnabled tanımlanmalıdır (KIM-D4).");
        }

        // KIM-D12: both rules are decided "off by default, can be turned on". What "on" means (which classes, which
        // age and what happens at expiry) is not decided yet, so turning either on stops the host instead of
        // guessing a rule.
        if (policy.RequireCharacterClasses is not false)
        {
            errors.Add($"{SectionName}:PasswordPolicy:RequireCharacterClasses tanımlanmalı ve şimdilik false olmalıdır; açık kuralı karara bağlı değil (KIM-D12).");
        }

        if (policy.MaximumAgeDays is not null)
        {
            errors.Add($"{SectionName}:PasswordPolicy:MaximumAgeDays şimdilik tanımlanamaz (kapalı); süre dolum davranışı karara bağlı değil (KIM-D12).");
        }

        if (policy.BlockedTerms is { } terms && terms.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{SectionName}:PasswordPolicy:BlockedTerms boş öğe içeremez.");
        }
    }

    /// <summary>The validated password policy; call only after <see cref="EnsureValid"/>.</summary>
    public LocalPasswordPolicy ToPasswordPolicy() =>
        new(
            PasswordPolicy.MinimumLength!.Value,
            PasswordPolicy.MaximumLength!.Value,
            PasswordPolicy.BlocklistEnabled!.Value,
            PasswordPolicy.BlockedTerms ?? []);

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

/// <summary>
/// Password policy (KIM-D4, KIM-D12). Starting values are decided (minimum length 8, blocklist on, character classes
/// and maximum age off) but every value stays configuration; the code has no default.
/// </summary>
public sealed class LocalAccountPasswordPolicySettings
{
    public string? PolicyRef { get; set; }

    public int? MinimumLength { get; set; }

    public int? MaximumLength { get; set; }

    public bool? RequireCharacterClasses { get; set; }

    public bool? BlocklistEnabled { get; set; }

    /// <summary>Extra blocked terms (common passwords); <c>admin</c>, the user name and company names are always added.</summary>
    public string[]? BlockedTerms { get; set; }

    public int? MaximumAgeDays { get; set; }
}

public sealed class LocalAccountRateLimitSettings
{
    public LocalAccountLoginRateLimitSettings Login { get; set; } = new();
}

/// <summary>
/// Login rate limit (KIM-D29: starting value 10 attempts per minute per IP address and normalized user name). It is
/// separate from the KIM-D4/KIM-D13 lockout counter and the value stays configurable.
/// </summary>
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
