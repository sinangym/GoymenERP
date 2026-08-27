using GoymenERP.Modules.Organization.Domain.Common;

namespace GoymenERP.Modules.Organization.Domain.Sites;

public sealed class Site : TenantEntity
{
    public Site(Guid id, Guid companyId, string code, string name, SiteType type)
        : base(id, companyId, code, name)
    {
        Type = type;
    }

    private Site()
    {
    }

    public SiteType Type { get; private set; }
}
