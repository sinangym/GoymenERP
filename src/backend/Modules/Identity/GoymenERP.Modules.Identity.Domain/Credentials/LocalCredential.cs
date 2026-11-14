namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// The local account credential of one user (ID-1; spec "Domain nesneleri", <c>identity.LocalCredentials</c>).
/// It is platform scoped like <c>UserAccount</c>: company access is still resolved from role assignments.
/// Only the password hash is stored, never the password. Login writes (<see cref="FailedAccessCount"/>,
/// <see cref="LockoutEndUtc"/>, a rehash) never increase <see cref="Version"/>; commands do.
/// </summary>
public sealed class LocalCredential
{
    private LocalCredential()
    {
    }

    public LocalCredential(
        Guid userId,
        string userName,
        string passwordHash,
        int hashFormatVersion,
        bool mustChangePassword,
        DateTimeOffset passwordChangedAtUtc,
        Guid securityStamp)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Kullanıcı kimliği boş olamaz.", nameof(userId));
        }

        var normalized = LocalUserName.TryNormalize(userName)
            ?? throw new ArgumentException("Kullanıcı adı geçerli değil.", nameof(userName));
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Parola özeti boş olamaz.", nameof(passwordHash));
        }

        if (hashFormatVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hashFormatVersion));
        }

        if (securityStamp == Guid.Empty)
        {
            throw new ArgumentException("Güvenlik damgası boş olamaz.", nameof(securityStamp));
        }

        UserId = userId;
        UserName = userName.Trim();
        NormalizedUserName = normalized;
        PasswordHash = passwordHash;
        HashFormatVersion = hashFormatVersion;
        MustChangePassword = mustChangePassword;
        PasswordChangedAtUtc = passwordChangedAtUtc;
        SecurityStamp = securityStamp;
        Version = 1;
    }

    public Guid UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string NormalizedUserName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public int HashFormatVersion { get; private set; }

    public bool MustChangePassword { get; private set; }

    public DateTimeOffset PasswordChangedAtUtc { get; private set; }

    public int FailedAccessCount { get; private set; }

    public DateTimeOffset? LockoutEndUtc { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>KIM-D23 temporary password; written by ID-2/ID-3 commands, read here only to fence login.</summary>
    public DateTimeOffset? TemporaryPasswordIssuedAtUtc { get; private set; }

    public DateTimeOffset? TemporaryPasswordExpiresAtUtc { get; private set; }

    public DateTimeOffset? TemporaryPasswordConsumedAtUtc { get; private set; }

    /// <summary>Renewed by password change/reset, deactivation and 2FA removal; sessions carry and re-check it.</summary>
    public Guid SecurityStamp { get; private set; }

    public long Version { get; private set; }

    /// <summary>
    /// Applies a failed attempt computed by <see cref="LocalCredentialLockout.AfterFailedAccess"/>. Used by
    /// non-relational stores; the SQL store writes the same values in one conditional UPDATE.
    /// </summary>
    public void ApplyFailedAccess(LocalCredentialFailureTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        FailedAccessCount = transition.FailedAccessCount;
        LockoutEndUtc = transition.LockoutEndUtc;
    }

    /// <summary>
    /// Applies a successful login: the counter and the lockout reset, an optional rehash replaces the hash. Used
    /// by non-relational stores; the SQL store writes the same values in one conditional UPDATE.
    /// </summary>
    public void ApplySuccessfulAccess(string? rehashedPasswordHash, int? rehashedFormatVersion)
    {
        if ((rehashedPasswordHash is null) != (rehashedFormatVersion is null))
        {
            throw new ArgumentException("Yeniden özet ve biçim sürümü birlikte verilmelidir.");
        }

        FailedAccessCount = 0;
        LockoutEndUtc = null;
        if (rehashedPasswordHash is not null)
        {
            PasswordHash = rehashedPasswordHash;
            HashFormatVersion = rehashedFormatVersion!.Value;
        }
    }
}
