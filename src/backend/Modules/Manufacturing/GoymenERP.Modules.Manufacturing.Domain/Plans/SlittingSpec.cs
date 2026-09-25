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
/// (widths, thickness, density, speed, diameters, tolerance) come from outside at approval, never from here.
/// </summary>
public sealed record SlittingSpec
{
    public SlittingSpec(IEnumerable<SlitLine> lines, Kilograms? windingTargetKg = null)
    {
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
    }

    public IReadOnlyList<SlitLine> Lines { get; }

    /// <summary>The mass each wound band coil is wound to, or null when the plan winds to no target.</summary>
    public Kilograms? WindingTargetKg { get; }

    public bool Equals(SlittingSpec? other) =>
        other is not null && WindingTargetKg == other.WindingTargetKg && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() =>
        Lines.Aggregate(WindingTargetKg.GetHashCode(), (hash, line) => HashCode.Combine(hash, line));
}
