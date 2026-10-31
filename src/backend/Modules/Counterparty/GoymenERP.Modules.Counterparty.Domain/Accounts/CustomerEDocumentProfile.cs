namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

/// <summary>
/// SAT-D119 (Sinan, 2026-10-31, EK17): the customer's e-belge profile is a fact about the customer's VKN/TCKN, shared
/// across all Göymen companies — a check recorded in one company is seen on the same-VKN customer's Cari card in the
/// others. It is therefore a GROUP-LEVEL Counterparty record keyed by the tax identity number, with no
/// <c>CompanyId</c> (the same shape as <c>Party</c>, DEC-CAR-001), never a per-company-account field. It holds the
/// e-Fatura / e-Arşiv status (SAT-D102: explicit <see cref="EDocumentStatus.Unknown"/>, never inferred), when, by whom
/// and IN WHICH COMPANY it was last checked, and the optional e-Fatura scenario override. The checking company is kept
/// so the checker's identity is shown only inside that company (other companies see the date and "checked in another
/// group company"). After a party's VKN changes (OPEN-030), its card simply reads the NEW VKN's profile; the old VKN's
/// profile stays as it is — never moved, merged or deleted — because the status is a fact of the VKN (SAT-D119). The allowed scenario codes are Settings
/// data (validated by the command before <see cref="SetScenarioOverride"/>); this type only keeps the lexical shape.
/// Read per company through an account that resolves to this VKN; company-private rows are never touched here.
/// </summary>
public sealed class CustomerEDocumentProfile
{
    private CustomerEDocumentProfile()
    {
    }

    /// <summary>
    /// Creates a profile for one taxpayer at version 0: status Unknown, no check, no override. A profile is only
    /// persisted after its first real change (<see cref="RecordStatus"/> / <see cref="SetScenarioOverride"/>), which
    /// moves it to version 1 — so the version counts real changes and a stored row is always at least 1.
    /// </summary>
    public CustomerEDocumentProfile(Guid id, string taxIdentityNumber, DateTimeOffset createdAtUtc)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", nameof(id)) : id;
        TaxIdentityNumber = NormalizeTaxIdentityNumber(taxIdentityNumber);
        EDocumentStatus = EDocumentStatus.Unknown;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>The taxpayer key: 10 (VKN) or 11 (TCKN) ASCII digits. Unique across the group (one profile per VKN).</summary>
    public string TaxIdentityNumber { get; private set; } = string.Empty;

    /// <summary>e-Fatura mükellefi / e-Arşiv; <see cref="EDocumentStatus.Unknown"/> until checked (never inferred).</summary>
    public EDocumentStatus EDocumentStatus { get; private set; } = EDocumentStatus.Unknown;

    /// <summary>When the status was last recorded as checked (server time); null exactly when Unknown.</summary>
    public DateTimeOffset? EDocumentStatusCheckedAtUtc { get; private set; }

    /// <summary>Who recorded the last check (the authenticated actor id); null exactly when Unknown.</summary>
    public string? EDocumentStatusCheckedByUserId { get; private set; }

    /// <summary>
    /// The company in whose context the last check was recorded; null exactly when Unknown. It is NOT a tenant key of
    /// the row (the row is group-wide): it only decides who may see the checker's identity (P3-1, SAT-D119).
    /// </summary>
    public Guid? EDocumentStatusCheckedInCompanyId { get; private set; }

    /// <summary>The customer's e-Fatura scenario code, overriding the Settings default; null = use the default.</summary>
    public string? EInvoiceScenarioOverrideCode { get; private set; }

    public long Version { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Records the e-belge status a user checked. A known status always counts as a new check (date and checker move,
    /// so the version moves and the change is audited) even when the value is unchanged; resetting to
    /// <see cref="EDocumentStatus.Unknown"/> clears the check. False only for Unknown → Unknown.
    /// </summary>
    public bool RecordStatus(
        EDocumentStatus status, string checkedByUserId, Guid checkedInCompanyId, DateTimeOffset nowUtc)
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
            EDocumentStatusCheckedInCompanyId = null;
            Touch(nowUtc);
            return true;
        }

        var checker = checkedByUserId?.Trim();
        if (string.IsNullOrEmpty(checker) || checker.Length > CheckerMaximumLength)
        {
            throw new ArgumentException("Kontrol eden kullanıcı kimliği geçersiz.", nameof(checkedByUserId));
        }

        if (checkedInCompanyId == Guid.Empty)
        {
            throw new ArgumentException("Kontrolün yapıldığı şirket boş olamaz.", nameof(checkedInCompanyId));
        }

        EDocumentStatus = status;
        EDocumentStatusCheckedAtUtc = nowUtc;
        EDocumentStatusCheckedByUserId = checker;
        EDocumentStatusCheckedInCompanyId = checkedInCompanyId;
        Touch(nowUtc);
        return true;
    }

    /// <summary>
    /// Sets (or, with null, clears) the e-Fatura scenario override. The caller has already proven the code is in the
    /// Settings code list in force for its company; false when the value is unchanged (no version move).
    /// </summary>
    public bool SetScenarioOverride(string? scenarioCode, DateTimeOffset nowUtc)
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
        Touch(nowUtc);
        return true;
    }

    private void Touch(DateTimeOffset nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        Version = checked(Version + 1);
    }

    /// <summary>The stored shape of a scenario code: 1..32, an ASCII upper-case letter first, then A-Z, 0-9 or '_'.</summary>
    public static bool IsWellFormedScenarioCode(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > ScenarioCodeMaximumLength || code[0] is < 'A' or > 'Z')
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

    private static string NormalizeTaxIdentityNumber(string taxIdentityNumber)
    {
        var value = taxIdentityNumber?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length is not (10 or 11) || !value.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Vergi/kimlik numarası 10 (VKN) veya 11 (TCKN) ASCII rakamı olmalıdır.",
                nameof(taxIdentityNumber));
        }

        return value;
    }

    /// <summary>Column length of the scenario override (the Settings code format's maximum).</summary>
    public const int ScenarioCodeMaximumLength = 32;

    /// <summary>Column length of the checker's actor id.</summary>
    public const int CheckerMaximumLength = 128;
}
