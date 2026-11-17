namespace GoymenERP.BuildingBlocks.Security;

/// <summary>KIM-D56: group authority is explicit; a company role never implies it.</summary>
public enum CompanySetupGroupOperationV1 { OpenCompany = 1, AssignFirstAdmin = 2 }

public sealed record CompanySetupGroupAuthoritySnapshotV1(bool IsGranted,
    CompanySetupGroupOperationV1 Operation, Guid ActorUserId, string Revision)
{
    public static CompanySetupGroupAuthoritySnapshotV1 Denied(CompanySetupGroupOperationV1 operation) =>
        new(false, operation, Guid.Empty, string.Empty);
}

/// <summary>Fresh Identity-owned stored eligibility, including before and after replay/read disclosure.</summary>
public interface ICompanySetupGroupAuthorityV1
{
    Task<CompanySetupGroupAuthoritySnapshotV1> ReadAsync(CompanySetupGroupOperationV1 operation,
        CancellationToken cancellationToken = default);
}
