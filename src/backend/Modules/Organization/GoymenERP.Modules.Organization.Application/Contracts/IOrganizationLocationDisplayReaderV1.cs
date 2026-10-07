namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// A bounded Organization-owned display read for another module's already-authorized response.
/// It resolves real location names without applying Organization user permissions or eligibility
/// filters: inactive and external locations remain displayable facts.
/// </summary>
public interface IOrganizationLocationDisplayReaderV1
{
    Task<IReadOnlyList<OrganizationLocationDisplayV1>> ReadAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Only the real Organization-owned display identity of a location.</summary>
public sealed record OrganizationLocationDisplayV1(
    Guid LocationId,
    string LocationCode,
    string LocationName);

/// <summary>
/// The batch must contain 1..100 distinct, non-empty ids. The consumer skips the read when it has
/// no locations; malformed batches fail closed before any Organization row is read.
/// </summary>
public sealed class OrganizationLocationDisplayQueryException : ArgumentException
{
    public const string ErrorCode = "organization_location_display_invalid_query";

    public OrganizationLocationDisplayQueryException()
        : base("Lokasyon gösterim okuması geçersiz.")
    {
    }

    public string Code => ErrorCode;
}
