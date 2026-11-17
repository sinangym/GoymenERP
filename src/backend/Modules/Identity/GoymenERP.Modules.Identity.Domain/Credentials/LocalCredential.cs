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
    /// Replaces the security stamp (KIM-D30 logout) and raises <see cref="Version"/> by one, because a stamp change
    /// is a command, not a login write. Used by non-relational stores; the SQL store writes the same values in one
    /// conditional UPDATE.
    /// </summary>
    public void RenewSecurityStamp(Guid newSecurityStamp)
    {
        if (newSecurityStamp == Guid.Empty)
        {
            throw new ArgumentException("Güvenlik damgası boş olamaz.", nameof(newSecurityStamp));
        }

        if (newSecurityStamp == SecurityStamp)
        {
            throw new ArgumentException("Yeni güvenlik damgası eskisinden farklı olmalıdır.", nameof(newSecurityStamp));
        }

        SecurityStamp = newSecurityStamp;
        Version++;
    }

    /// <summary>
    /// ID-2 password change (also the forced first change): the new hash replaces the old one, the forced-change
    /// flag and any temporary password are cleared, the lockout resets, the stamp is renewed (other sessions end,
    /// KS-21) and <see cref="Version"/> rises by one. Used by non-relational stores; the SQL store writes the same
    /// values in one conditional UPDATE.
    /// </summary>
    public void ChangePassword(string newPasswordHash, int hashFormatVersion, DateTimeOffset changedAtUtc, Guid newSecurityStamp)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new ArgumentException("Parola özeti boş olamaz.", nameof(newPasswordHash));
        }

        if (hashFormatVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hashFormatVersion));
        }

        if (newSecurityStamp == Guid.Empty || newSecurityStamp == SecurityStamp)
        {
            throw new ArgumentException("Yeni güvenlik damgası boş olamaz ve eskisinden farklı olmalıdır.", nameof(newSecurityStamp));
        }

        PasswordHash = newPasswordHash;
        HashFormatVersion = hashFormatVersion;
        MustChangePassword = false;
        PasswordChangedAtUtc = changedAtUtc;
        TemporaryPasswordIssuedAtUtc = null;
        TemporaryPasswordExpiresAtUtc = null;
        TemporaryPasswordConsumedAtUtc = null;
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        SecurityStamp = newSecurityStamp;
        Version++;
    }

    /// <summary>
    /// ID-2r <c>recover-founder reactivate</c> (KIM-D22): the founder must set a new password at the next login, the
    /// lockout resets, any temporary password is cleared, the stamp is renewed and <see cref="Version"/> rises by
    /// one. The password hash and the 2FA state are kept (the decision does not touch them, fail-closed).
    /// </summary>
    public void ApplyFounderReactivation(Guid newSecurityStamp)
    {
        RequireNewSecurityStamp(newSecurityStamp);

        MustChangePassword = true;
        TemporaryPasswordIssuedAtUtc = null;
        TemporaryPasswordExpiresAtUtc = null;
        TemporaryPasswordConsumedAtUtc = null;
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        SecurityStamp = newSecurityStamp;
        Version++;
    }

    /// <summary>
    /// ID-2r <c>recover-founder reactivate</c> with the password the operator typed at the terminal (spec ID-2r step 2:
    /// "yeni parola etkileşimli okunur"; "reset-password dışındaki her parola yazımı TemporaryPassword* alanlarını
    /// null'lar"): the new hash replaces the old one, the founder must still set his own password at the next login
    /// (<c>MustChangePassword=true</c>), any temporary password is cleared, the lockout resets, the stamp is renewed
    /// and <see cref="Version"/> rises by one. The 2FA state is kept (fail-closed).
    /// </summary>
    public void ApplyFounderReactivation(
        string newPasswordHash,
        int hashFormatVersion,
        DateTimeOffset changedAtUtc,
        Guid newSecurityStamp)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new ArgumentException("Parola özeti boş olamaz.", nameof(newPasswordHash));
        }

        if (hashFormatVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hashFormatVersion));
        }

        ApplyFounderReactivation(newSecurityStamp);
        PasswordHash = newPasswordHash;
        HashFormatVersion = hashFormatVersion;
        PasswordChangedAtUtc = changedAtUtc;
    }

    /// <summary>
    /// ID-2r <c>recover-founder reset-password</c> (KIM-D23): the temporary password hash replaces the old one, the
    /// founder must set a new password at the next login, the lockout resets, the temporary password window opens
    /// (issued, expires, not consumed), the stamp is renewed (every session ends) and <see cref="Version"/> rises by
    /// one. The lifetime is configuration owned by the caller; this method only refuses a window that is not in
    /// the future of its issue time.
    /// </summary>
    public void IssueTemporaryPassword(
        string temporaryPasswordHash,
        int hashFormatVersion,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        Guid newSecurityStamp)
    {
        if (string.IsNullOrWhiteSpace(temporaryPasswordHash))
        {
            throw new ArgumentException("Parola özeti boş olamaz.", nameof(temporaryPasswordHash));
        }

        if (hashFormatVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hashFormatVersion));
        }

        if (expiresAtUtc <= issuedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Geçici parolanın bitiş anı veriliş anından sonra olmalıdır.");
        }

        RequireNewSecurityStamp(newSecurityStamp);

        PasswordHash = temporaryPasswordHash;
        HashFormatVersion = hashFormatVersion;
        MustChangePassword = true;
        PasswordChangedAtUtc = issuedAtUtc;
        TemporaryPasswordIssuedAtUtc = issuedAtUtc;
        TemporaryPasswordExpiresAtUtc = expiresAtUtc;
        TemporaryPasswordConsumedAtUtc = null;
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        SecurityStamp = newSecurityStamp;
        Version++;
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

    private void RequireNewSecurityStamp(Guid newSecurityStamp)
    {
        if (newSecurityStamp == Guid.Empty || newSecurityStamp == SecurityStamp)
        {
            throw new ArgumentException("Yeni güvenlik damgası boş olamaz ve eskisinden farklı olmalıdır.", nameof(newSecurityStamp));
        }
    }

    /// <summary>KIM-D16 administrative reset: forced change, no founder temporary-password lifetime.</summary>
    public void ResetPassword(string hash, int formatVersion, DateTimeOffset now, Guid stamp)
    {
        ChangePassword(hash, formatVersion, now, stamp);
        MustChangePassword = true;
    }

    public void Unlock()
    {
        FailedAccessCount = 0;
        LockoutEndUtc = null;
        Version++;
    }

}

