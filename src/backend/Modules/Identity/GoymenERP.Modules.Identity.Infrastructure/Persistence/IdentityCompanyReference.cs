namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Organization's company, read-only (the table is Organization's and is never migrated or written here). ID-2 reads
/// it inside the bootstrap transaction (KIM-D18: Admin in every defined company, active and passive) and for the
/// company-name blocklist terms (KIM-D4).
/// </summary>
internal sealed class IdentityCompanyReference
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string LegalName { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
