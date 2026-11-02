namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Company-scoped, immutable Manufacturing tolerance reference data. The company always comes
/// from the server context; the result is neither close authority nor exception-approval authority.
/// A consumer persists the exact returned identity, version, value and window together.
/// </summary>
public interface ISettingsManufacturingToleranceProviderV1
{
    Task<ManufacturingToleranceProfileV1> GetManufacturingToleranceAsync(
        string process, DateOnly effectiveOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Üretim U1-b (URT-D6, URT-D23): the tolerance in force for one line (work center) of the process on the date, in this
    /// order: the work center's own published version covering the date; else the process's published version covering the
    /// date; else the reviewed version-0 catalog starting value (<see cref="ManufacturingProfileSourcesV1.Catalog"/>,
    /// fire 5 %, mass balance ± 2 %). Within one scope the latest <c>EffectiveFrom</c>, then the highest version. Only a
    /// window that covers the date is served (unlike the process-only read above). Codes: <c>settings_access_denied</c>;
    /// <see cref="ManufacturingParameterProfileCodesV1.RequestInvalid"/> for a non-canonical process or an empty work center
    /// id; <c>settings_provider_unavailable</c> for an unreadable store or a company with neither a catalog nor a published
    /// version; <see cref="SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound"/> when nothing covers the date.
    /// The default implementation (an adapter without line scope) answers the process-only read; Settings' own provider
    /// overrides it.
    /// </summary>
    Task<ManufacturingToleranceProfileV1> GetScopedManufacturingToleranceAsync(
        string process, Guid? workCenterId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        GetManufacturingToleranceAsync(process, effectiveOn, cancellationToken);
}

/// <summary>
/// Canonical cross-module identities for the five locked Manufacturing processes. These names
/// match the Manufacturing owner's public process contract; aliases and case folding are forbidden.
/// </summary>
public static class SettingsManufacturingProcessesV1
{
    public const string Slitting = "Slitting";
    public const string ColdRolling = "ColdRolling";
    public const string TubeProfile = "TubeProfile";
    public const string Paint = "Paint";
    public const string CutToLength = "CutToLength";

    public static bool IsCanonical(string? process) => process is
        Slitting or ColdRolling or TubeProfile or Paint or CutToLength;
}

public sealed record ManufacturingToleranceProfileV1(
    Guid CompanyId,
    string Process,
    ProfileVersionV1 Profile,
    decimal MaximumFirePercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough)
{
    /// <summary>
    /// URT-D6: the ± mass-balance difference percent of this profile version accepted without approval. Null for a version
    /// published before the field existed: unknown, never 0 — a consumer that needs it fails closed.
    /// </summary>
    public decimal? MassBalanceDifferenceTolerancePercent { get; init; }

    /// <summary>
    /// URT-D6: the line (work center) this version is scoped to; null for a process-wide version (and the catalog).
    /// </summary>
    public Guid? WorkCenterId { get; init; }

    /// <summary>
    /// URT-D23: <see cref="ManufacturingProfileSourcesV1.Settings"/> for a published registry version (≥ 1) or
    /// <see cref="ManufacturingProfileSourcesV1.Catalog"/> for the reviewed version-0 starting value. The consumer stores it
    /// with the version in its decision snapshot.
    /// </summary>
    public string Source { get; init; } = ManufacturingProfileSourcesV1.Settings;
}
