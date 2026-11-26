namespace GoymenERP.BuildingBlocks.Security;

/// <summary>
/// Stable technical identifiers for explicitly accepted ERP personas. These constants do not
/// create roles, seed assignments or grant permissions; Identity remains the owner of stored role
/// definitions and human-approved assignments.
/// </summary>
public static class ErpPersonaRoleCodes
{
    /// <summary>Sinan D2: the sales-support persona that may prepare orders but may not decide its own order.</summary>
    public const string SalesSupport = "SALES_SUPPORT";

    // ROL-D1/D4: technical IDs of the existing role proposal templates. These create no stored role or assignment.
    public const string GeneralManager = "GM";
    public const string SalesManager = "SATM";
    public const string PurchasingManager = "ALMM";
    public const string OperationsManager = "OPM";
    public const string FinanceManager = "FINM";
    public const string AccountingManager = "MUHM";
    public const string SystemAdministrator = "SYS";

    public static IReadOnlySet<string> ModuleManagers { get; } = new HashSet<string>([
        SalesManager, PurchasingManager, OperationsManager, FinanceManager, AccountingManager], StringComparer.Ordinal);
}
