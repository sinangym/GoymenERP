namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

/// <summary>
/// SAT-D57 (Sinan, 2026-10-27): a company's customer (price) group — the list a Cari account's group is chosen from.
/// The list belongs to ONE company and to the Counterparty module that owns the account it is assigned on; it is never
/// a shared party field. A group is never deleted: it is deactivated (passive) and may be reactivated; a passive group
/// keeps its existing assignments and their price and only refuses new ones (SAT-D77). No value is
/// seeded: the company's own administrator creates the list (Sinan's initial examples — toptancı, perakendeci,
/// imalatçı — are entered by a person, never by code or migration).
/// </summary>
public sealed class CustomerGroup
{
    public const int NameMaximumLength = 100;

    private CustomerGroup()
    {
    }

    public CustomerGroup(Guid id, Guid companyId, string name)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        Name = NormalizeName(name);
        var now = DateTimeOffset.UtcNow;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Renames the group; false when the normalized name is already its name (no version move).</summary>
    public bool Rename(string name)
    {
        var normalized = NormalizeName(name);
        if (string.Equals(Name, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        Name = normalized;
        Touch();
        return true;
    }

    /// <summary>Sets the active state; false when it already is that state (no version move).</summary>
    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        Touch();
        return true;
    }

    /// <summary>
    /// Trimmed, Unicode-normalized (NFC) text of 1..<see cref="NameMaximumLength"/> characters without control
    /// characters. Case is kept as typed; uniqueness within the company is the database's (collation) decision.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        var normalized = name?.Trim().Normalize(System.Text.NormalizationForm.FormC);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Müşteri grubu adı boş olamaz.", nameof(name));
        }

        if (normalized.Length > NameMaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Müşteri grubu adı {NameMaximumLength} karakterden uzun olamaz.");
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Müşteri grubu adı kontrol karakteri içeremez.", nameof(name));
        }

        return normalized;
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Version = checked(Version + 1);
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;
}
