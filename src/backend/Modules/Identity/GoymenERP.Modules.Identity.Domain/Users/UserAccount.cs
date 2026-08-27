namespace GoymenERP.Modules.Identity.Domain.Users;

public sealed class UserAccount
{
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
