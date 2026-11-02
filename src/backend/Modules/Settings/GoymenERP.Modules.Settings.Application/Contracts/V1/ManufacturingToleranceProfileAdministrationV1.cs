namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// API-02: the read name of the Manufacturing tolerance profile administration, beside the lifecycle names declared in
/// <see cref="ManufacturingToleranceProfileActionsV1"/>. Registered centrally, granted to no role; company-wide only.
/// </summary>
public static class ManufacturingToleranceProfileReadActionV1
{
    public const string Read = "settings.manufacturing-tolerance-profiles.read";
}

/// <summary>
/// Prepare a new draft tolerance profile version for one process (URT-D6, URT-D7). <c>ExpectedVersion</c> is the highest
/// profile version of the process ("0" while it has none). Percents are invariant decimal text, 0…100, at most six decimals:
/// <c>MaximumFirePercent</c> (above it the close needs the exception approval, URT-D7) and
/// <c>MassBalanceDifferenceTolerancePercent</c> (the ± mass-balance difference accepted without approval, URT-D6). The
/// published registry keeps windows of one process from overlapping and every published row immutable, so a profile that is
/// to be replaced later needs an <c>EffectiveThrough</c>.
/// </summary>
public sealed record PrepareManufacturingToleranceProfileRequestV1(
    string? ExpectedVersion,
    string? Process,
    string? MaximumFirePercent,
    string? MassBalanceDifferenceTolerancePercent,
    string? Reason,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveThrough = null);

/// <summary>
/// One tolerance profile version. <c>Source</c> <c>Catalog</c>: a reviewed starting value (URT-D6/D7) that is NOT read by the
/// close path (it reads published versions ≥ 1 only); <c>Settings</c>: a lifecycle version.
/// </summary>
public sealed record ManufacturingToleranceProfileViewV1(
    Guid ProfileId,
    string Source,
    string Process,
    string ProfileVersion,
    string StateVersion,
    string Status,
    string MaximumFirePercent,
    string? MassBalanceDifferenceTolerancePercent,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string? PreparedBy,
    DateTimeOffset? PreparedAtUtc,
    string PreparationReason,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? ApprovalReason,
    string? PublishedBy,
    DateTimeOffset? PublishedAtUtc,
    string? PublicationReason);

/// <summary>The company's tolerance profile versions (every status) and the reviewed starting values.</summary>
public sealed record ManufacturingToleranceProfileListViewV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    IReadOnlyList<ManufacturingToleranceProfileViewV1> Profiles,
    IReadOnlyList<ManufacturingToleranceProfileViewV1> StartingValues);

/// <summary>
/// The write path of the Settings Manufacturing tolerance registry (D-MFG-PROFILE-AUTH-01=A lifecycle): prepare, approve
/// (approver ≠ preparer), publish (publisher ≠ preparer). Publishing writes the immutable registry row the tolerance provider
/// reads, in the same transaction as the lifecycle state, the command answer and the audit event.
/// </summary>
public interface ISettingsManufacturingToleranceProfileAdministrationV1
{
    Task<ManufacturingToleranceProfileListViewV1> ListAsync(CancellationToken cancellationToken = default);

    Task<ManufacturingToleranceProfileViewV1?> GetAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<ManufacturingProfileCommandResultV1> PrepareAsync(PrepareManufacturingToleranceProfileRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ManufacturingProfileCommandResultV1> ApproveAsync(Guid profileId, ManufacturingProfileLifecycleRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ManufacturingProfileCommandResultV1> PublishAsync(Guid profileId, ManufacturingProfileLifecycleRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
