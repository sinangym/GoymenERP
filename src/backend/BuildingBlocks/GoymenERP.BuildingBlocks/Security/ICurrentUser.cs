namespace GoymenERP.BuildingBlocks.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string? UserId { get; }

    IReadOnlySet<Guid> AllowedCompanyIds { get; }

    bool CanAccessCompany(Guid companyId);
}
