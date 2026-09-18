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
    /// Manufacturing API (MFG-API, #313): the owner's technical mapping of its routes (Business
    /// #254 5654539215), accepted by the coordinator (K-1). Registering the names makes them KNOWN,
    /// not granted: no role and no user receives them here; the grant is a human gate (K-3).
    /// Company-wide scope is required (K-1): the routes do not filter by work center, so a
    /// work-center-scoped assignment would otherwise reach every work center of the company. Not
    /// Critical: no source makes plan creation and approval a segregated duty (K-2).
    /// </summary>
    public const string ManufacturingPlansWrite = "manufacturing.plans.write";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingPlansApprove = "manufacturing.plans.approve";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingOrdersCreate = "manufacturing.orders.create";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingFactsRecord = "manufacturing.facts.record";

    /// <inheritdoc cref="ManufacturingPlansWrite"/>
    public const string ManufacturingOrdersRead = "manufacturing.orders.read";

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
            ManufacturingPlansWrite,
            ManufacturingPlansApprove,
            ManufacturingOrdersCreate,
            ManufacturingFactsRecord,
            ManufacturingOrdersRead,
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
            ManufacturingPlansWrite,
            ManufacturingPlansApprove,
            ManufacturingOrdersCreate,
            ManufacturingFactsRecord,
            ManufacturingOrdersRead,
            IdentityAuditRead,
        ],
        StringComparer.Ordinal);

    public static bool IsKnown(string permission) => All.Contains(permission);

    public static bool IsCritical(string permission) => Critical.Contains(permission);

    public static bool RequiresCompanyWideScope(string permission) =>
        CompanyWideOnly.Contains(permission);
}
