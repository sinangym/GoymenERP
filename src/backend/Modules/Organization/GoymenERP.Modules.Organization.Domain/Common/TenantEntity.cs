namespace GoymenERP.Modules.Organization.Domain.Common;

public abstract class TenantEntity
{
    protected TenantEntity(Guid id, Guid companyId, string code, string name)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        Code = RequireText(code, nameof(code), 64);
        Name = RequireText(name, nameof(name), 200);
    }

    protected TenantEntity()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    internal void Touch(DateTimeOffset utcNow)
    {
        UpdatedAtUtc = utcNow;
        Version++;
    }

    protected static Guid RequireId(Guid value, string parameterName)
    {
        return value == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", parameterName)
            : value;
    }

    protected static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Değer {maximumLength} karakterden uzun olamaz.");
        }

        return normalized;
    }
}
