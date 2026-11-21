namespace GoymenERP.Modules.Identity.Domain.Users;

public sealed class UserAccount
{
    /// <summary>Upper bound of <see cref="DisplayName"/> (column <c>nvarchar(200)</c>).</summary>
    public const int DisplayNameMaxLength = 200;

    private UserAccount()
    {
    }

    public UserAccount(Guid id, string displayName, string? email)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Kullanıcı kimliği boş olamaz.", nameof(id));
        }

        Id = id;
        DisplayName = Required(displayName, nameof(displayName));
        Email = NormalizeOptional(email);
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public long Version { get; private set; }

    public void Deactivate(DateTimeOffset changedAtUtc)
    {
        if (!IsActive) throw new InvalidOperationException("Kullanıcı zaten pasif.");
        IsActive = false;
        UpdatedAtUtc = changedAtUtc;
        Version++;
    }

    /// <summary>
    /// ID-2r <c>recover-founder reactivate</c> (KIM-D22): an inactive account becomes active again and
    /// <see cref="Version"/> rises by one. No role assignment is created; access comes back only through the
    /// assignments that are still valid. Reactivating an active account is a caller error, never a silent no-op.
    /// </summary>
    public void Reactivate(DateTimeOffset reactivatedAtUtc)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("Etkin bir kullanıcı yeniden etkinleştirilemez.");
        }

        IsActive = true;
        UpdatedAtUtc = reactivatedAtUtc;
        Version++;
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
