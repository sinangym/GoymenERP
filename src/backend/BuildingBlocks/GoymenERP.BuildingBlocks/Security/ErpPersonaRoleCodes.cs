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
}
