namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

public sealed class CompanyPartyAccount
{
    private const PartyRole KnownRoles =
        PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;

    private CompanyPartyAccount()
    {
    }

    public CompanyPartyAccount(
        Guid id,
        Guid companyId,
        Guid partyId,
        PartyRole roles,
        string? externalIntegrationCode = null)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        PartyId = RequireId(partyId, nameof(partyId));
        Roles = RequireRoles(roles);
        ExternalIntegrationCode = NormalizeOptionalText(
            externalIntegrationCode,
            nameof(externalIntegrationCode),
            128);
        var now = DateTimeOffset.UtcNow;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid PartyId { get; private set; }

    public PartyRole Roles { get; private set; }

    public string? ExternalIntegrationCode { get; private set; }

    public bool IsActive { get; private set; } = true;

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool HasRole(PartyRole role)
    {
        if (role == PartyRole.None || (role & ~KnownRoles) != 0)
        {
            return false;
        }

        return (Roles & role) == role;
    }

    public void SetRoles(PartyRole roles)
    {
        var validated = RequireRoles(roles);
        if (Roles == validated)
        {
            return;
        }

        Roles = validated;
        Touch(DateTimeOffset.UtcNow);
    }

    public void SetExternalIntegrationCode(string? externalIntegrationCode)
    {
        var normalized = NormalizeOptionalText(
            externalIntegrationCode,
            nameof(externalIntegrationCode),
            128);
        if (string.Equals(ExternalIntegrationCode, normalized, StringComparison.Ordinal))
        {
            return;
        }

        ExternalIntegrationCode = normalized;
        Touch(DateTimeOffset.UtcNow);
    }

    public void Deactivate()
    {
        if (IsActive)
        {
            IsActive = false;
        }

        Touch(DateTimeOffset.UtcNow);
    }

    public void Reactivate()
    {
        if (!IsActive)
        {
            IsActive = true;
        }

        Touch(DateTimeOffset.UtcNow);
    }

    internal void Touch(DateTimeOffset utcNow)
    {
        UpdatedAtUtc = utcNow;
        Version++;
    }

    private static PartyRole RequireRoles(PartyRole roles)
    {
        if ((roles & ~KnownRoles) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roles),
                roles,
                "CariHesap en az bir bilinen ticari rol taşımalıdır.");
        }

        return roles;
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        return value == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", parameterName)
            : value;
    }

    private static string? NormalizeOptionalText(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
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
