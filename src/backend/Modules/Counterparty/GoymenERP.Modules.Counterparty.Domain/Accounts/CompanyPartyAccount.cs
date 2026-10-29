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

    public string? ContactInformationJson { get; private set; }

    /// <summary>
    /// SAT-D57: the company's customer (price) group this account is in, or none. A group of THIS company only (the
    /// database's composite foreign key on CompanyId + CustomerGroupId); no group means the price chain's group tier is
    /// skipped for this account.
    /// </summary>
    public Guid? CustomerGroupId { get; private set; }

    /// <summary>SAT-D102: the e-belge status (e-Fatura mükellefi / e-Arşiv); <see cref="EDocumentStatus.Unknown"/> until checked.</summary>
    public EDocumentStatus EDocumentStatus { get; private set; } = EDocumentStatus.Unknown;

    /// <summary>SAT-D102: when the status was last recorded as checked (server time); null exactly when Unknown.</summary>
    public DateTimeOffset? EDocumentStatusCheckedAtUtc { get; private set; }

    /// <summary>SAT-D102: who recorded the last check (the authenticated actor id); null exactly when Unknown.</summary>
    public string? EDocumentStatusCheckedByUserId { get; private set; }

    /// <summary>
    /// SAT-D102: the customer's own e-Fatura scenario code, overriding the Settings default; null = use the default.
    /// The allowed codes are the Settings code list's (validated by the command before it calls
    /// <see cref="SetEInvoiceScenarioOverride"/>); this type only keeps the lexical shape.
    /// </summary>
    public string? EInvoiceScenarioOverrideCode { get; private set; }

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

    internal void UpdateContactInformation(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var nextVersion = checked(Version + 1);
        ContactInformationJson = json;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Version = nextVersion;
    }

    /// <summary>
    /// SAT-D57: assigns (or, with null, clears) the account's customer group. The caller has already proven that the
    /// group belongs to this account's company and is active; false when the value is unchanged (no version move).
    /// </summary>
    public bool AssignCustomerGroup(Guid? customerGroupId)
    {
        if (customerGroupId == Guid.Empty)
        {
            throw new ArgumentException("Müşteri grubu kimliği boş olamaz.", nameof(customerGroupId));
        }

        if (CustomerGroupId == customerGroupId)
        {
            return false;
        }

        CustomerGroupId = customerGroupId;
        Touch(DateTimeOffset.UtcNow);
        return true;
    }

    /// <summary>
    /// SAT-D102: records the e-belge status a user checked. A known status always counts as a new check (the date and
    /// checker move, so the version moves and the change is audited) even when the value is the same; resetting to
    /// <see cref="EDocumentStatus.Unknown"/> clears the check date and checker. False only for Unknown → Unknown.
    /// </summary>
    public bool RecordEDocumentStatus(EDocumentStatus status, string checkedByUserId, DateTimeOffset checkedAtUtc)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Bilinmeyen e-belge durumu.");
        }

        if (status == EDocumentStatus.Unknown)
        {
            if (EDocumentStatus == EDocumentStatus.Unknown)
            {
                return false;
            }

            EDocumentStatus = EDocumentStatus.Unknown;
            EDocumentStatusCheckedAtUtc = null;
            EDocumentStatusCheckedByUserId = null;
            Touch(DateTimeOffset.UtcNow);
            return true;
        }

        var checker = checkedByUserId?.Trim();
        if (string.IsNullOrEmpty(checker) || checker.Length > EDocumentCheckerMaximumLength)
        {
            throw new ArgumentException("Kontrol eden kullanıcı kimliği geçersiz.", nameof(checkedByUserId));
        }

        EDocumentStatus = status;
        EDocumentStatusCheckedAtUtc = checkedAtUtc;
        EDocumentStatusCheckedByUserId = checker;
        Touch(DateTimeOffset.UtcNow);
        return true;
    }

    /// <summary>
    /// SAT-D102: sets (or, with null, clears) the customer's e-Fatura scenario override. The caller has already proven
    /// that the code is in the Settings code list in force; false when the value is unchanged (no version move).
    /// </summary>
    public bool SetEInvoiceScenarioOverride(string? scenarioCode)
    {
        if (scenarioCode is not null && !IsWellFormedScenarioCode(scenarioCode))
        {
            throw new ArgumentException("e-Fatura senaryo kodu biçimi geçersiz.", nameof(scenarioCode));
        }

        if (string.Equals(EInvoiceScenarioOverrideCode, scenarioCode, StringComparison.Ordinal))
        {
            return false;
        }

        EInvoiceScenarioOverrideCode = scenarioCode;
        Touch(DateTimeOffset.UtcNow);
        return true;
    }

    /// <summary>The stored shape of a scenario code: 1..32, an ASCII upper-case letter first, then A-Z, 0-9 or '_'.</summary>
    public static bool IsWellFormedScenarioCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > EInvoiceScenarioCodeMaximumLength || code[0] is < 'A' or > 'Z')
        {
            return false;
        }

        foreach (var character in code)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Column length of the scenario override (the Settings code format's maximum).</summary>
    public const int EInvoiceScenarioCodeMaximumLength = 32;

    /// <summary>Column length of the checker's actor id.</summary>
    public const int EDocumentCheckerMaximumLength = 128;

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
