namespace GoymenERP.BuildingBlocks.Multitenancy;

public interface ICompanyContext
{
    bool IsResolved { get; }

    Guid CompanyId { get; }

    void Set(Guid companyId);
}
