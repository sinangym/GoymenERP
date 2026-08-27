namespace GoymenERP.Modules.Organization.Domain.Companies;

public sealed class Company
{
    public Company(Guid id, string code, string legalName, bool isPlaceholder)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(id));
        }

        Id = id;
        Code = RequireText(code, nameof(code), 64);
        LegalName = RequireText(legalName, nameof(legalName), 250);
        IsPlaceholder = isPlaceholder;
    }

    private Company()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string LegalName { get; private set; } = string.Empty;

    public bool IsPlaceholder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(parameterName)
            : normalized;
    }
}
