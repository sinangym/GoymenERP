namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>One client-identified coil-to-sheet cut line. Sequence is an explicit lossless value and need not be contiguous.</summary>
public sealed record CutToLengthLine
{
    public CutToLengthLine(Guid lineId, int sequence, decimal lengthMm, int count)
    {
        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("Kesim satırı kimliği boş olamaz.", nameof(lineId));
        }

        if (lengthMm <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMm), lengthMm, "Kesim boyu pozitif olmalıdır.");
        }

        if (DecimalScale(lengthMm) > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMm), lengthMm, "Kesim boyu en çok üç ondalık taşımalıdır.");
        }

        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Kesim adedi pozitif olmalıdır.");
        }

        LineId = lineId;
        Sequence = sequence;
        LengthMm = lengthMm;
        Count = count;
    }

    public Guid LineId { get; }
    public int Sequence { get; }
    public decimal LengthMm { get; }
    public int Count { get; }

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;
}

/// <summary>
/// Ordered coil-to-sheet cut lines carried by a plan. Input order, explicit sequence and client identity are content.
/// </summary>
public sealed class CutToLengthSpec
{
    private readonly IReadOnlyList<CutToLengthLine> _lines;

    public CutToLengthSpec(IEnumerable<CutToLengthLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Any(line => line is null))
        {
            throw new ArgumentException("Kesim satırı boş olamaz.", nameof(lines));
        }

        if (materialized.Length == 0)
        {
            throw new ArgumentException("Kesim planı en az bir satır taşımalıdır.", nameof(lines));
        }

        if (materialized.Select(line => line.LineId).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Kesim satırı kimliği plan içinde tekil olmalıdır.", nameof(lines));
        }

        _lines = Array.AsReadOnly(materialized);
    }

    public IReadOnlyList<CutToLengthLine> Lines => _lines;
}
