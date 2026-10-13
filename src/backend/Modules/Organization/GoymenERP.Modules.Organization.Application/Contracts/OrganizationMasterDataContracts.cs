using System.Text.Json.Serialization;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;

namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// The Organization owner's technical permission names for the site / location / work-centre master edit
/// (first-release outcome "create and edit sites, locations and work centres"). Names only: nothing here registers,
/// classifies or grants a permission. Registration in the central catalogue is the central writer's step; which
/// persona holds which is a human gate.
/// </summary>
public static class OrganizationPermissionManifestV1
{
    public const int Version = 1;
    public const string Subject = "Organization";

    /// <summary>
    /// POST /api/v1/organization/sites and PUT …/sites/{id}. A site has no parent inside the company, so the grant is
    /// meaningful only company-wide (the central catalogue is asked to register it as company-wide only); a narrower
    /// scope answers the same code-less 404 as an unknown record.
    /// </summary>
    public const string SitesWrite = "organization.sites.write";

    /// <summary>
    /// POST /api/v1/organization/locations (the parent site must be inside the grant's scope) and PUT …/locations/{id}
    /// (the location read predicate on this grant). Site / location / work-centre scoped, like
    /// <c>organization.locations.status.update</c>. It does not confer the active-state change.
    /// </summary>
    public const string LocationsWrite = "organization.locations.write";

    /// <summary>
    /// POST /api/v1/organization/work-centers (the parent location must be inside the grant's scope) and
    /// PUT …/work-centers/{id} (the work-centre read predicate on this grant). Site / location / work-centre scoped.
    /// </summary>
    public const string WorkCentersWrite = "organization.work-centers.write";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(
        [SitesWrite, LocationsWrite, WorkCentersWrite],
        StringComparer.Ordinal);
}

/// <summary>The one stable problem code of the master edit; every other refusal stays code-less like the module's reads.</summary>
public static class OrganizationProblemCodesV1
{
    /// <summary>
    /// 422 — the code is already used by another record of the same kind in the resolved company (the
    /// <c>(CompanyId, Code)</c> unique index). Active and inactive records both hold their code.
    /// </summary>
    public const string CodeInUse = "organization_code_in_use";

    public const string LocationOperatorIneligible = "organization_location_operator_ineligible";

    public const string LocationOperatorDependencyUnavailable = "organization_location_operator_dependency_unavailable";
}

/// <summary>
/// POST /api/v1/organization/sites. <paramref name="Type"/> is the <c>SiteType</c> token as the site read publishes it
/// (<c>Factory</c>, <c>Warehouse</c>, <c>ExternalWarehouse</c>); a location created under an <c>ExternalWarehouse</c>
/// site is external, exactly as the seeded topology derives it.
/// </summary>
public sealed record CreateSite(
    string Code,
    string Name,
    string Type,
    string Reason);

/// <summary>PUT /api/v1/organization/sites/{id}: code and name only; the site type is fixed at creation.</summary>
public sealed record UpdateSite(
    string Code,
    string Name,
    [property: JsonRequired] long ExpectedVersion,
    string Reason);

/// <summary>
/// POST /api/v1/organization/locations. The external flag is not a request field: it follows the parent site's type
/// (<c>ExternalWarehouse</c> → external), the same derivation the runtime seed uses (DEC-ORG-004, OPEN-002). The
/// operator / owner party of an external depot is not modelled (OPEN-002 backlog) and is not accepted here.
/// </summary>
public sealed record CreateLocation(
    [property: JsonRequired] Guid SiteId,
    string Code,
    string Name,
    string Reason);

/// <summary>PUT /api/v1/organization/locations/{id}: code and name only; the site and the external flag are fixed.</summary>
public sealed record UpdateLocation(
    string Code,
    string Name,
    [property: JsonRequired] long ExpectedVersion,
    string Reason);

/// <summary>
/// POST /api/v1/organization/work-centers. <paramref name="Kind"/> is the <c>WorkCenterKind</c> token as the
/// work-centre read publishes it. The kind is the Manufacturing planning reference's immutable fact, so it is chosen
/// here once and never edited.
/// </summary>
public sealed record CreateWorkCenter(
    [property: JsonRequired] Guid LocationId,
    string Code,
    string Name,
    string Kind,
    string Reason);

/// <summary>PUT /api/v1/organization/work-centers/{id}: code and name only; the location and the kind are fixed.</summary>
public sealed record UpdateWorkCenter(
    string Code,
    string Name,
    [property: JsonRequired] long ExpectedVersion,
    string Reason);

/// <summary>The 201 body of a create: the new record's id and its first version, replayed verbatim for a repeated key.</summary>
public sealed record OrganizationCreatedRecord(
    Guid Id,
    long Version);

/// <summary>
/// The site / location / work-centre master edit. Every command is idempotent on its key (a repeated key with the same
/// payload and actor replays the stored terminal answer and applies nothing again), company-fenced by the resolved
/// company context, scope-checked on its own write permission, and writes one audit event per applied command.
/// Kept apart from <see cref="IOrganizationService"/> so the reads' test doubles are not widened.
/// </summary>
public interface IOrganizationMasterDataService
{
    Task<CommandExecutionResult> CreateSiteAsync(
        CreateSite command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> UpdateSiteAsync(
        Guid id,
        UpdateSite command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> CreateLocationAsync(
        CreateLocation command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> UpdateLocationAsync(
        Guid id,
        UpdateLocation command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> SetLocationOperatorAsync(
        Guid id,
        SetLocationOperator command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> CreateWorkCenterAsync(
        CreateWorkCenter command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CommandExecutionResult> UpdateWorkCenterAsync(
        Guid id,
        UpdateWorkCenter command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>
/// The request-shape rules shared by the endpoint (400 before anything runs) and the service (which refuses the same
/// shapes as a programming error): text lengths are the persisted column limits after trim.
/// </summary>
public static class OrganizationMasterDataRules
{
    public const int CodeMaximumLength = 64;
    public const int NameMaximumLength = 200;
    public const int ReasonMaximumLength = 1000;

    /// <summary>The site type tokens exactly as the site read publishes them (<c>SiteType.ToString()</c>).</summary>
    public static readonly IReadOnlyList<string> SiteTypes = Enum.GetNames<SiteType>();

    /// <summary>The work-centre kind tokens exactly as the work-centre read publishes them (<c>WorkCenterKind.ToString()</c>).</summary>
    public static readonly IReadOnlyList<string> WorkCenterKinds = Enum.GetNames<WorkCenterKind>();

    public static bool IsValidCode(string? code) => IsText(code, CodeMaximumLength);

    public static bool IsValidName(string? name) => IsText(name, NameMaximumLength);

    public static bool IsValidReason(string? reason) => IsText(reason, ReasonMaximumLength);

    public static bool IsSiteType(string? type) => type is not null && SiteTypes.Contains(type, StringComparer.Ordinal);

    public static bool IsWorkCenterKind(string? kind) =>
        kind is not null && WorkCenterKinds.Contains(kind, StringComparer.Ordinal);

    private static bool IsText(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= maximumLength
            && !trimmed.Any(char.IsControl);
    }
}
