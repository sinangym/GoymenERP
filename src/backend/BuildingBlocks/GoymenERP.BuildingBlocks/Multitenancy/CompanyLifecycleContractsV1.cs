namespace GoymenERP.BuildingBlocks.Multitenancy;

/// <summary>KIM-D20: setup is independent of operational IsActive; unknown/unreadable never authorizes work.</summary>
public enum CompanyLifecycleStateV1 { SetupPending = 1, Active = 2 }
public sealed record CompanyLifecycleViewV1(Guid CompanyId, CompanyLifecycleStateV1 State, bool IsActive, long Version);
public interface ICompanyLifecycleReadPortV1
{
    Task<CompanyLifecycleViewV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken = default);
}
