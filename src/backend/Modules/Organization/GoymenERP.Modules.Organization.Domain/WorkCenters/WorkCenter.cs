using GoymenERP.Modules.Organization.Domain.Common;

namespace GoymenERP.Modules.Organization.Domain.WorkCenters;

public sealed class WorkCenter : TenantEntity
{
    public WorkCenter(
        Guid id,
        Guid companyId,
        Guid locationId,
        string code,
        string name,
        WorkCenterKind kind)
        : base(id, companyId, code, name)
    {
        LocationId = RequireId(locationId, nameof(locationId));
        Kind = kind;
    }

    private WorkCenter()
    {
    }

    public Guid LocationId { get; private set; }

    public WorkCenterKind Kind { get; private set; }
}
