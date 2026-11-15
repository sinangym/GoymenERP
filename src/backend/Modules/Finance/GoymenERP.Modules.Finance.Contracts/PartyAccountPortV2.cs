namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// B-17 (KALAN_MODULLER_HAZIRLIK_PLANI_v1 §3): the roles a company party account carries, as Finance reads them
/// through <see cref="IFinancePartyAccountPortV2"/>. DEC-CAR-001: one legal counterparty is one party record and the
/// same party may be customer, supplier, tolling and service at once, so these are flags, never one role per copy.
/// <para>
/// The values are Finance's own: the host adapter maps the Counterparty owner's roles flag by flag and refuses a
/// flag it does not know (fail closed) instead of casting, so a new owner role never reaches Finance unnoticed.
/// </para>
/// <para>
/// Reading the roles decides nothing. Which Finance operation needs which role (for example whether a payment
/// requires the supplier role) is a money policy that no decision locks yet; no Finance service refuses on a role
/// because of this port.
/// </para>
/// </summary>
[Flags]
public enum FinancePartyAccountRoles
{
    None = 0,
    Customer = 1,
    Supplier = 2,
    Tolling = 4,
    Service = 8,
}

/// <summary>
/// B-17: what Finance knows about the supplier IBAN of a party account. Every state but
/// <see cref="StructurallyValid"/> carries no IBAN text.
/// </summary>
public enum FinanceSupplierIbanState
{
    /// <summary>The account does not carry the supplier role; a supplier IBAN is not asked for.</summary>
    NotApplicable,

    /// <summary>
    /// The account is a supplier, but the Counterparty owner does not provide an approved supplier IBAN source yet
    /// (FNS-D15: "ertelendi: Cari başlayınca"). Never read as "no IBAN recorded".
    /// </summary>
    SourceUnavailable,

    /// <summary>The owner answered and holds no approved supplier IBAN for this account.</summary>
    NotRecorded,

    /// <summary>
    /// The owner's approved IBAN passed <see cref="FinanceIban.IsStructurallyValid"/>. Structure only: K2 says
    /// "IBAN biçim/checksum doğrulaması banka mülkiyeti/onayı yerine geçmez".
    /// </summary>
    StructurallyValid,

    /// <summary>The owner's value failed the structural check; the malformed text is not released.</summary>
    StructurallyInvalid,
}

/// <summary>The supplier IBAN view of <see cref="PartyAccountEligibilityV2"/>; <see cref="Iban"/> is normalized.</summary>
public sealed record FinanceSupplierIbanView(FinanceSupplierIbanState State, string? Iban);

/// <summary>
/// The owner's answer for the supplier IBAN of one party account, before Finance validates it. Only an APPROVED
/// value may be handed here (CARI-DEC-004 keeps an IBAN change maker-checker in Counterparty); a pending change
/// never is. <see cref="IsSourceAvailable"/> false means the owner could not answer, not that there is no IBAN.
/// </summary>
public sealed record FinanceSupplierIbanSourceAnswer(bool IsSourceAvailable, string? ApprovedIban)
{
    public static FinanceSupplierIbanSourceAnswer Unavailable { get; } = new(false, null);
}

/// <summary>
/// The one place the supplier IBAN of a party account is classified, so every adapter of
/// <see cref="IFinancePartyAccountPortV2"/> applies the same structural rule (<see cref="FinanceIban"/>) and none
/// re-implements it. It derives nothing from the IBAN (K2: "eksik banka verisi icat edilmez").
/// </summary>
public static class FinanceSupplierIbanRules
{
    public static FinanceSupplierIbanView Classify(FinancePartyAccountRoles roles, FinanceSupplierIbanSourceAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if ((roles & FinancePartyAccountRoles.Supplier) == 0)
        {
            return new(FinanceSupplierIbanState.NotApplicable, null);
        }

        if (!answer.IsSourceAvailable)
        {
            return new(FinanceSupplierIbanState.SourceUnavailable, null);
        }

        var normalized = FinanceIban.Normalize(answer.ApprovedIban);
        if (normalized.Length == 0)
        {
            return new(FinanceSupplierIbanState.NotRecorded, null);
        }

        return FinanceIban.IsStructurallyValid(normalized)
            ? new(FinanceSupplierIbanState.StructurallyValid, normalized)
            : new(FinanceSupplierIbanState.StructurallyInvalid, null);
    }
}

/// <summary>
/// B-17: <see cref="PartyAccountEligibility"/> plus the account's roles (dual role, DEC-CAR-001) and its supplier
/// IBAN view. The label is mandatory here: a V2 answer without the owner's versions is not a Found answer.
/// </summary>
public sealed record PartyAccountEligibilityV2(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CompanyId,
    bool IsActive,
    bool IsPartyActive,
    FinancePartyAccountRoles Roles,
    DraftPartyLabel Label,
    FinanceSupplierIbanView SupplierIban)
{
    public bool IsCustomer => (Roles & FinancePartyAccountRoles.Customer) != 0;

    public bool IsSupplier => (Roles & FinancePartyAccountRoles.Supplier) != 0;

    /// <summary>DEC-CAR-001 / FINANS_HAZIRLIK_PLANI §1: the same firm is both customer and supplier.</summary>
    public bool IsCustomerAndSupplier => IsCustomer && IsSupplier;
}

/// <summary>
/// B-17 version bump of <see cref="IFinancePartyAccountPort"/>. The V1 port stays unchanged until every Finance
/// consumer has moved (PANO §1: the old version stays while its consumers migrate).
/// <para>
/// Same fences as V1: the adapter answers only for the server-resolved company and actor of
/// <c>context</c>, re-checks both after its awaited owner read, and returns Denied rather than any data
/// or NotFound when either changed meanwhile. Unavailable means the owner's answer could not be mapped safely.
/// </para>
/// </summary>
public interface IFinancePartyAccountPortV2
{
    Task<ReadResult<PartyAccountEligibilityV2>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
        Guid companyAccountId, CancellationToken cancellationToken);
}
