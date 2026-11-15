namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable unit conversion policy version of a company (F-004; BS-9/BS-13/BS-16/BS-17, URN-D7…D9; insert-only). The
/// codes are stored as their published names, so a stored row reads back without a numbering table.
/// </summary>
internal sealed class UnitConversionPolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public decimal DensityGramsPerCubicCentimetre { get; set; }
    public int CoefficientDecimals { get; set; }
    public string CoefficientRounding { get; set; } = string.Empty;
    public int DocumentTotalDecimals { get; set; }
    public string DocumentTotalRounding { get; set; } = string.Empty;
    public string WholeUnitRounding { get; set; } = string.Empty;
    public decimal WeighbridgeTolerancePercent { get; set; }
    public string ToleranceBoundary { get; set; } = string.Empty;
    public string ToleranceBase { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
