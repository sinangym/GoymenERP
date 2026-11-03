using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// One slit line of a plan: a band width (nominal millimetres, Ö2: at most three decimals, refused
/// beyond, never rounded) and how many bands of it are cut from each parent roll (master v3 §28.4
/// SLT-002).
/// </summary>
public sealed record SlitLine
{
    public const int MillimetreScale = 3;

    public SlitLine(decimal bandWidthMm, int count)
    {
        if (bandWidthMm <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(bandWidthMm), bandWidthMm, "Bant eni sıfırdan büyük olmalıdır.");
        }

        if (decimal.Round(bandWidthMm, MillimetreScale) != bandWidthMm)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.MillimetrePrecisionExceeded,
                $"Milimetre en fazla {MillimetreScale} ondalık taşıyabilir.");
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Bant adedi sıfırdan büyük olmalıdır.");
        }

        BandWidthMm = bandWidthMm;
        Count = count;
    }

    public decimal BandWidthMm { get; }

    public int Count { get; }
}

/// <summary>
/// What a slitting plan cuts (MFG-6 slice 2): the slit lines applied to each parent roll and, when the
/// bands are wound to a mass, the winding target (WND-001…004 input). It is part of the plan's content:
/// set when the plan is created, never changed afterwards. Its engine inputs that are not the plan's own
/// (thickness, parent width, density, diameters, tolerance) come from outside at approval, never from here.
/// Üretim U2-a (plan v1 §8, URT-D9): the optional <see cref="NetUsableWidthMm"/> is entered on the plan — the width
/// the slit lines may use (SLT-001…007) — and is checked against each parent coil's captured width by the plan.
/// </summary>
public sealed record SlittingSpec
{
    public SlittingSpec(IEnumerable<SlitLine> lines, Kilograms? windingTargetKg = null, decimal? netUsableWidthMm = null)
    {
        if (netUsableWidthMm is { } netWidth)
        {
            if (netWidth <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(netUsableWidthMm), netUsableWidthMm,
                    "Net kullanılabilir en sıfırdan büyük olmalıdır.");
            }

            if (decimal.Round(netWidth, SlitLine.MillimetreScale) != netWidth)
            {
                throw new ManufacturingRuleException(ManufacturingRuleCodes.MillimetrePrecisionExceeded,
                    $"Milimetre en fazla {SlitLine.MillimetreScale} ondalık taşıyabilir.");
            }
        }

        ArgumentNullException.ThrowIfNull(lines);
        Lines = lines.ToArray();
        if (Lines.Any(line => line is null))
        {
            throw new ArgumentException("Dilme satırı boş olamaz.", nameof(lines));
        }

        if (Lines.Count == 0)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.SlittingSpecWithoutLines,
                "Dilme planı en az bir dilme satırı içermelidir.");
        }

        if (windingTargetKg is { Value: 0m })
        {
            throw new ArgumentOutOfRangeException(nameof(windingTargetKg), windingTargetKg, "Sarım hedefi sıfırdan büyük olmalıdır.");
        }

        WindingTargetKg = windingTargetKg;
        NetUsableWidthMm = netUsableWidthMm;
    }

    public IReadOnlyList<SlitLine> Lines { get; }

    /// <summary>The mass each wound band coil is wound to, or null when the plan winds to no target.</summary>
    public Kilograms? WindingTargetKg { get; }

    /// <summary>
    /// Üretim U2-a: the net usable width (nominal millimetres, Ö2) entered on the plan, or null when it was not entered
    /// (the approval gate then names <c>netUsableWidthMm</c> missing). Never defaulted from the coil width.
    /// </summary>
    public decimal? NetUsableWidthMm { get; }

    public bool Equals(SlittingSpec? other) =>
        other is not null && WindingTargetKg == other.WindingTargetKg && NetUsableWidthMm == other.NetUsableWidthMm
        && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() =>
        Lines.Aggregate(HashCode.Combine(WindingTargetKg, NetUsableWidthMm), (hash, line) => HashCode.Combine(hash, line));
}
