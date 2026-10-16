namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Slitting;

/// <summary>
/// The physical parent-coil mass and the three widths needed to allocate that mass. Edge trim is
/// derived as <c>PhysicalWidthMm - NetUsableWidthMm</c>; it is not an actual scrap declaration.
/// </summary>
public sealed record SlittingMassInputV2(
    decimal? ParentPhysicalKg,
    decimal? PhysicalWidthMm,
    decimal? NetUsableWidthMm,
    decimal? UsedBandWidthMm);

/// <summary>
/// The theoretical allocation of one physical parent-coil mass. All values are unrounded and the
/// three masses add exactly to the supplied physical mass.
/// </summary>
public sealed record SlittingMassAllocationV2(
    string FormulaVersion,
    decimal EdgeTrimWidthMm,
    decimal BandKg,
    decimal TrimKg,
    decimal NetRemainingKg);

/// <summary>
/// Slitting formula version 2. It allocates physical mass over physical width so the edge-trim share
/// remains explicit. It does not select production policy, infer density or tolerance, or declare
/// theoretical trim as actual scrap.
/// </summary>
public static class SlittingEngineV2
{
    public const string FormulaVersion = "GDC-SLT/2";

    public const string NetWidthExceedsPhysicalWidthCode = "engine_net_width_exceeds_physical_width";

    public static EngineResult<SlittingMassAllocationV2> Allocate(SlittingMassInputV2 input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var inputs = new EngineInputs();
        var physicalKg = inputs.Measure(input.ParentPhysicalKg, "parentPhysicalKg");
        var physicalWidth = inputs.Measure(input.PhysicalWidthMm, "physicalWidthMm");
        var netWidth = inputs.Measure(input.NetUsableWidthMm, "netUsableWidthMm");
        var usedBandWidth = inputs.Measure(input.UsedBandWidthMm, "usedBandWidthMm");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<SlittingMassAllocationV2>();
        }

        if (netWidth > physicalWidth)
        {
            return EngineResult<SlittingMassAllocationV2>.Refused(
                NetWidthExceedsPhysicalWidthCode,
                "netUsableWidthMm");
        }

        if (usedBandWidth > netWidth)
        {
            return EngineResult<SlittingMassAllocationV2>.Refused(
                EngineCodes.UsedWidthExceedsNetWidth,
                "usedBandWidthMm");
        }

        var edgeTrimWidth = physicalWidth - netWidth;
        var bandKg = physicalKg * usedBandWidth / physicalWidth;
        var trimKg = physicalKg * edgeTrimWidth / physicalWidth;

        // This is algebraically PhysicalKg × (NetWidth − UsedBandWidth) / PhysicalWidth. Evaluating
        // it as the conservation residual prevents independent decimal divisions from creating a
        // one-unit residue; no business rounding is introduced.
        var netRemainingKg = physicalKg - bandKg - trimKg;

        return EngineResult<SlittingMassAllocationV2>.Computed(new SlittingMassAllocationV2(
            FormulaVersion,
            edgeTrimWidth,
            bandKg,
            trimKg,
            netRemainingKg));
    }
}
