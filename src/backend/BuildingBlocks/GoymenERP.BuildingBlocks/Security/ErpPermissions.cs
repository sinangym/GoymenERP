namespace GoymenERP.BuildingBlocks.Security;

public static class ErpPermissions
{
    public const string OrganizationContextRead = "organization.context.read";
    public const string OrganizationSitesRead = "organization.sites.read";
    public const string OrganizationLocationsRead = "organization.locations.read";
    public const string OrganizationWorkCentersRead = "organization.work-centers.read";
    public const string OrganizationLocationsStatusUpdate =
        "organization.locations.status.update";

    public const string CounterpartyPartiesRead = "counterparty.parties.read";
    public const string CounterpartyPartiesCreate = "counterparty.parties.create";
    public const string CounterpartyPartiesUpdateBasic = "counterparty.parties.update.basic";
    public const string CounterpartyAccountsCreate = "counterparty.accounts.create";
    public const string CounterpartyAccountsStatusUpdate =
        "counterparty.accounts.status.update";

    /// <summary>
    /// The narrow operational product-selection read (K-1(a), Sinan's decision relayed in Business
    /// #66 5719251344; registration asked for in 5720016574 §3). Registering the name makes it
    /// KNOWN, not granted: no role and no user receives it here, and an actor without an explicit
    /// assignment still gets <see cref="PermissionScope.Denied"/>. It is not the Product master
    /// read and carries no master management, cost/price history, audit or unbounded listing.
    /// Company-wide scope is required: the read answers for the server-resolved company and has no
    /// site, location or work-center dimension, so a narrower assignment would have nothing to mean
    /// (same reason the Counterparty reads are company-wide only).
    /// </summary>
    public const string ProductOperationalSelectionRead = "product.operational-selection.read";

    /// <summary>
    /// Finance bank-account master (FS0, #312): names are the Finance owner's technical mapping
    /// (Business #254 5654539215). Registering them makes them KNOWN, not granted: no role and no
    /// user receives them here, and who holds which is a human gate. All three are company-wide
    /// only: a bank account belongs to the server-resolved company and has no site, location or
    /// work-center dimension. Propose and approve are Critical: the security contract lists the
    /// bank account among critical permissions (docs/security/AI_YETKI_SOZLESMESI.md) and Sinan's
    /// Ö4 (#66 5722637822) makes the approval maker-checker, so their GRANT follows ADR-004 §6
    /// (the requester or target cannot approve their own request). Read is not Critical: it shows
    /// only the approved account's label and currency needed to pick it in a draft.
    /// </summary>
    public const string FinanceBankAccountRead = "finance.bank-account.read";
    public const string FinanceBankAccountPropose = "finance.bank-account.propose";
    public const string FinanceBankAccountApprove = "finance.bank-account.approve";

    public const string IdentityProfileRead = "identity.profile.read";
    public const string IdentityAccessRequest = "identity.access.request";
    public const string IdentityAccessApprove = "identity.access.approve";
    public const string IdentityRolesManage = "identity.roles.manage";
    public const string IdentityAuditRead = "identity.audit.read";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            OrganizationContextRead,
            OrganizationSitesRead,
            OrganizationLocationsRead,
            OrganizationWorkCentersRead,
            OrganizationLocationsStatusUpdate,
            CounterpartyPartiesRead,
            CounterpartyPartiesCreate,
            CounterpartyPartiesUpdateBasic,
            CounterpartyAccountsCreate,
            CounterpartyAccountsStatusUpdate,
            ProductOperationalSelectionRead,
            FinanceBankAccountRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
            IdentityProfileRead,
            IdentityAccessRequest,
            IdentityAccessApprove,
            IdentityRolesManage,
            IdentityAuditRead,
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> Critical { get; } = new HashSet<string>(
        [
            IdentityAccessApprove,
            IdentityRolesManage,
            IdentityAuditRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
        ],
        StringComparer.Ordinal);

    public static IReadOnlySet<string> CompanyWideOnly { get; } = new HashSet<string>(
        [
            CounterpartyPartiesRead,
            CounterpartyPartiesCreate,
            CounterpartyPartiesUpdateBasic,
            CounterpartyAccountsCreate,
            CounterpartyAccountsStatusUpdate,
            ProductOperationalSelectionRead,
            FinanceBankAccountRead,
            FinanceBankAccountPropose,
            FinanceBankAccountApprove,
            IdentityAuditRead,
        ],
        StringComparer.Ordinal);

    public static bool IsKnown(string permission) => All.Contains(permission);

    public static bool IsCritical(string permission) => Critical.Contains(permission);

    public static bool RequiresCompanyWideScope(string permission) =>
        CompanyWideOnly.Contains(permission);
}
