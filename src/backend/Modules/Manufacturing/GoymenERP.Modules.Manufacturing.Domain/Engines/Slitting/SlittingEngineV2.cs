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
/// An exact, immutable mass value represented as a canonical rational number. The denominator is
/// always positive, numerator and denominator are reduced by their greatest common divisor, and
/// zero is always represented as <c>0/1</c>.
/// </summary>
public sealed record ExactMassV2
{
    public ExactMassV2(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "The denominator cannot be zero.");
        }

        if (numerator.IsZero)
        {
            Numerator = BigInteger.Zero;
            Denominator = BigInteger.One;
            return;
        }

        if (denominator.Sign < 0)
        {
            numerator = BigInteger.Negate(numerator);
            denominator = BigInteger.Negate(denominator);
        }

        var common = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / common;
        Denominator = denominator / common;
    }

    public BigInteger Numerator { get; }

    public BigInteger Denominator { get; }

    public static ExactMassV2 FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var numerator = (BigInteger)(uint)bits[0]
            | (BigInteger)(uint)bits[1] << 32
            | (BigInteger)(uint)bits[2] << 64;
        if ((bits[3] & int.MinValue) != 0)
        {
            numerator = BigInteger.Negate(numerator);
        }

        var scale = (bits[3] >> 16) & 0xff;
        return new ExactMassV2(numerator, BigInteger.Pow(10, scale));
    }

    public static ExactMassV2 operator +(ExactMassV2 left, ExactMassV2 right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new ExactMassV2(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);
    }
}

/// <summary>
/// The theoretical allocation of one physical parent-coil mass. All values are unrounded and the
/// three masses add exactly to the supplied physical mass.
/// </summary>
public sealed record SlittingMassAllocationV2(
    string FormulaVersion,
    decimal EdgeTrimWidthMm,
    ExactMassV2 BandKg,
    ExactMassV2 TrimKg,
    ExactMassV2 NetRemainingKg);

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

    private static ExactMassV2 ProportionalMass(decimal mass, decimal width, decimal physicalWidth)
    {
        var exactMass = ExactMassV2.FromDecimal(mass);
        var exactWidth = ExactMassV2.FromDecimal(width);
        var exactPhysicalWidth = ExactMassV2.FromDecimal(physicalWidth);

        return new ExactMassV2(
            exactMass.Numerator * exactWidth.Numerator * exactPhysicalWidth.Denominator,
            exactMass.Denominator * exactWidth.Denominator * exactPhysicalWidth.Numerator);
    }
}
