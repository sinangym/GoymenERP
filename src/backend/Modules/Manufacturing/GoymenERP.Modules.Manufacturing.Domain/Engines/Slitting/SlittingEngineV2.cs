using System.Numerics;

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
        var bandKg = ProportionalMass(physicalKg, usedBandWidth, physicalWidth);
        var trimKg = ProportionalMass(physicalKg, edgeTrimWidth, physicalWidth);
        var netRemainingKg = ProportionalMass(physicalKg, netWidth - usedBandWidth, physicalWidth);

        return EngineResult<SlittingMassAllocationV2>.Computed(new SlittingMassAllocationV2(
            FormulaVersion,
            edgeTrimWidth,
            bandKg,
            trimKg,
            netRemainingKg));
    }

    /// <summary>
    /// Evaluates <c>mass * width / physicalWidth</c> as one exact rational before converting to the
    /// nearest representable decimal. <see cref="BigInteger"/> avoids an overflowing multiplication;
    /// the conversion uses the greatest decimal scale that fits the CLR decimal coefficient and
    /// midpoint-to-even, matching decimal arithmetic rather than introducing a business rounding rule.
    /// </summary>
    private static decimal ProportionalMass(decimal mass, decimal width, decimal physicalWidth)
    {
        if (width == 0m)
        {
            return 0m;
        }

        var (massCoefficient, massScale) = Parts(mass);
        var (widthCoefficient, widthScale) = Parts(width);
        var (physicalCoefficient, physicalScale) = Parts(physicalWidth);

        var numerator = massCoefficient * widthCoefficient;
        var denominator = physicalCoefficient;
        var scaleAdjustment = massScale + widthScale - physicalScale;
        if (scaleAdjustment > 0)
        {
            denominator *= BigInteger.Pow(10, scaleAdjustment);
        }
        else if (scaleAdjustment < 0)
        {
            numerator *= BigInteger.Pow(10, -scaleAdjustment);
        }

        var common = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return ToDecimal(numerator / common, denominator / common);
    }

    private static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0]
            | (BigInteger)(uint)bits[1] << 32
            | (BigInteger)(uint)bits[2] << 64;
        return (coefficient, (bits[3] >> 16) & 0x7f);
    }

    private static decimal ToDecimal(BigInteger numerator, BigInteger denominator)
    {
        var maximumCoefficient = (BigInteger.One << 96) - 1;
        for (var scale = 28; scale >= 0; scale--)
        {
            var scaledNumerator = numerator * BigInteger.Pow(10, scale);
            var coefficient = BigInteger.DivRem(scaledNumerator, denominator, out var remainder);
            var doubledRemainder = remainder << 1;
            if (doubledRemainder > denominator || (doubledRemainder == denominator && !coefficient.IsEven))
            {
                coefficient++;
            }

            if (coefficient <= maximumCoefficient)
            {
                var low = (uint)(coefficient & uint.MaxValue);
                var middle = (uint)((coefficient >> 32) & uint.MaxValue);
                var high = (uint)((coefficient >> 64) & uint.MaxValue);
                return new decimal((int)low, (int)middle, (int)high, false, (byte)scale);
            }
        }

        throw new OverflowException("The proportional mass does not fit in a decimal.");
    }
}
