using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Accounting.UnitTests;

// Adversarial port double: production CompanyContext deliberately forbids this drift.
internal sealed class MutableCompanyContextForFenceTests(Guid companyId) : ICompanyContext
{
    public bool IsResolved => CompanyId != Guid.Empty;
    public Guid CompanyId { get; private set; } = companyId;
    public void Set(Guid value) => CompanyId = value;
}
