using GoymenERP.Modules.Organization.Domain.Common;

namespace GoymenERP.Modules.Organization.Domain.Locations;

public sealed class Location : TenantEntity
{
    public Location(
        Guid id,
        Guid companyId,
        Guid siteId,
        string code,
        string name,
        bool isExternal)
        : base(id, companyId, code, name)
    {
        SiteId = RequireId(siteId, nameof(siteId));
        IsExternal = isExternal;
    }

    private Location()
    {
    }

    public Guid SiteId { get; private set; }

    public bool IsExternal { get; private set; }
}
