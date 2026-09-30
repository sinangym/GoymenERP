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
    DateOnly? EffectiveThrough);
