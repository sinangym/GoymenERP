namespace GoymenERP.BuildingBlocks.Security;

/// <summary>
/// Reads the current actor's active role codes for one exact company directly from Identity role
/// assignments. The answer is fresh on every call and is intentionally independent of role
/// permissions: a persona must not disappear merely because one permission was removed or supplied
/// by another role.
/// </summary>
public interface IActiveRoleCodeReader
{
    Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}
