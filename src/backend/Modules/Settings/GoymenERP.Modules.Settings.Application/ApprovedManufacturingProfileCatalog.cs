using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One reviewed version-0 parameter profile of the catalog (process scope; no work center).</summary>
public sealed record ApprovedManufacturingParameterProfileEntry(
    Guid CompanyId,
    string Process,
    Guid ProfileId,
    DateOnly EffectiveFrom,
    IReadOnlyList<ManufacturingParameterValueDataV1> Values);

/// <summary>One reviewed tolerance starting value (URT-D6/D7).</summary>
public sealed record ApprovedManufacturingToleranceStartingValue(
    Guid CompanyId,
    string Process,
    Guid ProfileId,
    DateOnly EffectiveFrom,
    decimal MaximumFirePercent,
    decimal MassBalanceDifferenceTolerancePercent);

/// <summary>
/// URT-D6, URT-D7, URT-D9, URT-D10 (Sinan, 2026-11-01, <c>docs/decisions/SINAN_KARARLARI_2026-11-01_URETIM.md</c>): the reviewed
/// starting values of the Manufacturing parameter and tolerance profiles — StokERP's values, the SAME in each of GDC, GSP and
/// GIN (each company its own entries) — as versioned source data in the shape of <see cref="ApprovedTaxItemTypeDefaultCatalog"/>:
/// a new reviewed value is a new version reviewed like code, never a constant inside a calculator.
/// <list type="table">
/// <item><term>Parameters (version 0, the BASE of each process timeline)</term><description>Steel density 7,85 kg/dm³ =
/// 7850 kg/m³ (the engines' unit) for Slitting, Cold Rolling and Cut to Length (URT-D9); painting line speed 20 m/min, film
/// 80 µm, solids ratio 0,55, paint loss factor 1,10, 3 simultaneous profiles (URT-D9); kerf 0 mm on the Cut to Length profile
/// and edge trim ("kenar kaybı") 0 mm on the Slitting profile (URT-D10). Slitting and tube line speeds are entered on the plan,
/// so no line speed is shipped for them and Tube / Profile has no catalog profile. The kerf mode, head/tail trim, coil
/// diameters and metre tolerance are not decided and are not shipped (unknown, never 0). A Settings version published later
/// replaces a catalog profile from its own date; a work center's own profile wins over it (URT-D9 "hat bazında").</description></item>
/// <item><term>Tolerance starting values</term><description>Maximum fire 5 % per process (URT-D7) and the ± 2 % mass-balance
/// difference (URT-D6). The close path reads only published registry versions (version ≥ 1, <c>OrderClosureAssessment</c>), so
/// these are offered as the starting values of the first published version, not read by the close path.</description></item>
/// </list>
/// Ids are UUIDv5 in namespace <c>e9a41596-9abd-5184-a339-27195ede7be1</c> (parameters, = UUIDv5(URL,
/// "https://github.com/sinangym/GoymenERP/settings/manufacturing-parameter-profiles"); names
/// <c>manufacturing-parameter-profile:&lt;company id&gt;:&lt;process&gt;:v0</c>) and <c>dc0475bb-dc23-5f9e-852b-a31064ff2c37</c>
/// (tolerance, "…/settings/manufacturing-tolerance-profiles"; <c>manufacturing-tolerance-profile:…:v0</c>); never reused.
/// </summary>
public static class ApprovedManufacturingProfileCatalog
{
    /// <summary>Below every Settings version (those start at 1), so a same-day Settings version wins.</summary>
    public const long CatalogProfileVersion = 0;

    public const string Source =
        "URT-D6/D7/D9/D10 (SINAN_KARARLARI_2026-11-01_URETIM.md); StokERP's starting values as Sinan's explicit decision";

    public static readonly DateOnly EffectiveFrom = new(2026, 11, 1);

    private static readonly Guid[] Companies =
    [
        ApprovedUnitProfileCatalog.GdcCompanyId,
        ApprovedUnitProfileCatalog.GspCompanyId,
        ApprovedUnitProfileCatalog.GinCompanyId,
    ];

    public static IReadOnlyList<ApprovedManufacturingParameterProfileEntry> ParameterProfiles { get; } = BuildParameters();

    public static IReadOnlyList<ApprovedManufacturingToleranceStartingValue> ToleranceStartingValues { get; } = BuildTolerance();

    public static IReadOnlyList<ApprovedManufacturingParameterProfileEntry> ParametersFor(Guid companyId) =>
        ParameterProfiles.Where(entry => entry.CompanyId == companyId).ToArray();

    public static IReadOnlyList<ApprovedManufacturingToleranceStartingValue> ToleranceFor(Guid companyId) =>
        ToleranceStartingValues.Where(entry => entry.CompanyId == companyId).ToArray();

    public static bool Serves(Guid companyId) => Companies.Contains(companyId);

    private static ApprovedManufacturingParameterProfileEntry[] BuildParameters()
    {
        // (company, Slitting, ColdRolling, CutToLength, Paint)
        (Guid Company, Guid Slitting, Guid ColdRolling, Guid CutToLength, Guid Paint)[] ids =
        [
            (ApprovedUnitProfileCatalog.GdcCompanyId, Guid.Parse("37f2c5ca-56a1-54d7-a39e-e77a821934fc"),
                Guid.Parse("845f44a2-e704-5b4a-a37c-47085ff248da"), Guid.Parse("c3e0cdb4-b835-58af-a3e7-701d7836df90"),
                Guid.Parse("424ceaf8-b4d0-5a68-9c4f-189c32a26b55")),
            (ApprovedUnitProfileCatalog.GspCompanyId, Guid.Parse("bc40508a-1961-55b4-a6ba-671e583c90d2"),
                Guid.Parse("8feb1cbb-0cd6-5d6c-964d-c4cd819e0aac"), Guid.Parse("e7a863f6-a81e-51f4-a729-2d0c8b845da7"),
                Guid.Parse("ccc305ff-620f-51ff-a05d-1da6ec6d55fd")),
            (ApprovedUnitProfileCatalog.GinCompanyId, Guid.Parse("b74baa8e-95fd-5c98-9033-b93cf6881496"),
                Guid.Parse("91d2b43e-0007-578c-839c-9135ec602ba0"), Guid.Parse("ee86d346-a79a-52aa-9f4c-ea3f72a08c5e"),
                Guid.Parse("b7e77e9f-d4f7-56b8-b7d4-2603b805078d")),
        ];
        var entries = ids.SelectMany(company => new[]
        {
            Parameters(company.Company, SettingsManufacturingProcessesV1.Slitting, company.Slitting,
                (ManufacturingParameterKeysV1.DensityKgPerCubicMetre, "7850"), (ManufacturingParameterKeysV1.EdgeTrimMm, "0")),
            Parameters(company.Company, SettingsManufacturingProcessesV1.ColdRolling, company.ColdRolling,
                (ManufacturingParameterKeysV1.DensityKgPerCubicMetre, "7850")),
            Parameters(company.Company, SettingsManufacturingProcessesV1.CutToLength, company.CutToLength,
                (ManufacturingParameterKeysV1.DensityKgPerCubicMetre, "7850"), (ManufacturingParameterKeysV1.KerfMm, "0")),
            Parameters(company.Company, SettingsManufacturingProcessesV1.Paint, company.Paint,
                (ManufacturingParameterKeysV1.LineSpeedMetresPerMinute, "20"),
                (ManufacturingParameterKeysV1.FilmMicrons, "80"),
                (ManufacturingParameterKeysV1.SolidsRatio, "0.55"),
                (ManufacturingParameterKeysV1.ScrapFactor, "1.10"),
                (ManufacturingParameterKeysV1.SimultaneousProfiles, "3")),
        }).ToArray();
        if (entries.Select(entry => entry.ProfileId).Distinct().Count() != entries.Length)
        {
            throw Invalid();
        }

        return entries;
    }

    private static ApprovedManufacturingParameterProfileEntry Parameters(Guid companyId, string process, Guid profileId,
        params (string Key, string Value)[] values)
    {
        // The catalog passes the same validation as a prepared Settings version: a malformed entry fails at type load.
        var input = values.Select(value => new ManufacturingParameterValueInputV1(value.Key, value.Value)).ToArray();
        if (!ManufacturingProfileValueRulesV1.TryReadValues(process, input, out var data, out _))
        {
            throw Invalid();
        }

        return new ApprovedManufacturingParameterProfileEntry(companyId, process, profileId, EffectiveFrom, data);
    }

    private static ApprovedManufacturingToleranceStartingValue[] BuildTolerance()
    {
        // URT-D7: 5 % per process; URT-D6: ± 2 % per line (one profile per process applies to each of its lines).
        const string maximumFirePercent = "5";
        const string massBalancePercent = "2";
        if (!ManufacturingProfileValueRulesV1.TryReadPercent(maximumFirePercent, out var fire)
            || !ManufacturingProfileValueRulesV1.TryReadPercent(massBalancePercent, out var massBalance))
        {
            throw Invalid();
        }

        (Guid Company, string Process, Guid ProfileId)[] ids =
        [
            (ApprovedUnitProfileCatalog.GdcCompanyId, SettingsManufacturingProcessesV1.Slitting, Guid.Parse("9fa01453-0872-5e82-ae05-a1bda37d8c55")),
            (ApprovedUnitProfileCatalog.GdcCompanyId, SettingsManufacturingProcessesV1.ColdRolling, Guid.Parse("36394354-3543-5963-a75d-af39975a2bd7")),
            (ApprovedUnitProfileCatalog.GdcCompanyId, SettingsManufacturingProcessesV1.TubeProfile, Guid.Parse("3e5ad493-ea04-5dcd-8f0a-56f9048632bd")),
            (ApprovedUnitProfileCatalog.GdcCompanyId, SettingsManufacturingProcessesV1.Paint, Guid.Parse("77fcc884-d156-5e3e-a76a-2187dfb42c42")),
            (ApprovedUnitProfileCatalog.GdcCompanyId, SettingsManufacturingProcessesV1.CutToLength, Guid.Parse("65a30258-80a3-55dc-a4c5-bec95218f565")),
            (ApprovedUnitProfileCatalog.GspCompanyId, SettingsManufacturingProcessesV1.Slitting, Guid.Parse("f9ced63c-ddef-547f-92e2-d3e36dee2c20")),
            (ApprovedUnitProfileCatalog.GspCompanyId, SettingsManufacturingProcessesV1.ColdRolling, Guid.Parse("f3afb471-f4fa-505e-84d6-69396f3f49df")),
            (ApprovedUnitProfileCatalog.GspCompanyId, SettingsManufacturingProcessesV1.TubeProfile, Guid.Parse("b4c70120-61b1-5e66-a016-aba6d35c7e49")),
            (ApprovedUnitProfileCatalog.GspCompanyId, SettingsManufacturingProcessesV1.Paint, Guid.Parse("97cf9aff-4ef1-59d3-a9ef-f75e8eca8c3f")),
            (ApprovedUnitProfileCatalog.GspCompanyId, SettingsManufacturingProcessesV1.CutToLength, Guid.Parse("c940f4d9-e532-592a-a973-8f0d06fef80e")),
            (ApprovedUnitProfileCatalog.GinCompanyId, SettingsManufacturingProcessesV1.Slitting, Guid.Parse("4b67f065-d604-55e1-854d-2d586b624dcd")),
            (ApprovedUnitProfileCatalog.GinCompanyId, SettingsManufacturingProcessesV1.ColdRolling, Guid.Parse("662dd639-62f7-5ad1-bf86-f860e70de0ba")),
            (ApprovedUnitProfileCatalog.GinCompanyId, SettingsManufacturingProcessesV1.TubeProfile, Guid.Parse("8f1ab4a7-98d8-596e-8e56-27a3d1e34710")),
            (ApprovedUnitProfileCatalog.GinCompanyId, SettingsManufacturingProcessesV1.Paint, Guid.Parse("c59f9590-5a98-53d6-86a5-4555a19ff143")),
            (ApprovedUnitProfileCatalog.GinCompanyId, SettingsManufacturingProcessesV1.CutToLength, Guid.Parse("1f2f5ba1-f856-50e1-a6ac-5ebb409fb876")),
        ];
        return ids.Select(id => new ApprovedManufacturingToleranceStartingValue(id.Company, id.Process, id.ProfileId,
            EffectiveFrom, fire, massBalance)).ToArray();
    }

    private static SettingsPrerequisiteException Invalid() => new("settings_profile_invalid");
}
