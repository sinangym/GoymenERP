namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// One source-ordered cold-rolling pass. <see cref="SourceSequence"/> is a lossless source value;
/// it is not required to be positive, unique or contiguous and does not reorder the containing list.
/// </summary>
public sealed record ColdRollingPassLine
{
    public ColdRollingPassLine(
        Guid lineId,
        int sourceSequence,
        decimal inputThicknessMm,
        decimal inputWidthMm,
        decimal outputThicknessMm,
        decimal outputWidthMm)
    {
        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("Hadde paso satırı kimliği boş olamaz.", nameof(lineId));
        }

        RequireMeasure(inputThicknessMm, nameof(inputThicknessMm));
        RequireMeasure(inputWidthMm, nameof(inputWidthMm));
        RequireMeasure(outputThicknessMm, nameof(outputThicknessMm));
        RequireMeasure(outputWidthMm, nameof(outputWidthMm));
        if (outputThicknessMm >= inputThicknessMm)
        {
            throw new ArgumentException("Her hadde pasosu kalınlığı azaltmalıdır.", nameof(outputThicknessMm));
        }

        LineId = lineId;
        SourceSequence = sourceSequence;
        InputThicknessMm = inputThicknessMm;
        InputWidthMm = inputWidthMm;
        OutputThicknessMm = outputThicknessMm;
        OutputWidthMm = outputWidthMm;
    }

    public Guid LineId { get; }
    public int SourceSequence { get; }
    public decimal InputThicknessMm { get; }
    public decimal InputWidthMm { get; }
    public decimal OutputThicknessMm { get; }
    public decimal OutputWidthMm { get; }

    private static void RequireMeasure(decimal value, string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Paso ölçüsü pozitif olmalıdır.");
        }

        if (DecimalScale(value) > 3)
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                "Paso ölçüsü en çok üç ondalık taşımalıdır.");
        }
    }

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;
}

/// <summary>
/// One immutable versioned cold-rolling recipe. List order is the process order. Adjacent passes must
/// meet on exactly the same thickness and width; no tolerance or rounding is applied.
/// </summary>
public sealed class ColdRollingPassSpec
{
    private readonly IReadOnlyList<ColdRollingPassLine> _lines;

    public ColdRollingPassSpec(Guid passRecipeId, string version, IEnumerable<ColdRollingPassLine> lines)
    {
        if (passRecipeId == Guid.Empty)
        {
            throw new ArgumentException("Paso reçetesi kimliği boş olamaz.", nameof(passRecipeId));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Paso reçetesi sürümü boş olamaz.", nameof(version));
        }

        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Any(line => line is null))
        {
            throw new ArgumentException("Paso satırı boş olamaz.", nameof(lines));
        }

        if (materialized.Length == 0)
        {
            throw new ArgumentException("Paso reçetesi en az bir satır taşımalıdır.", nameof(lines));
        }

        if (materialized.Select(line => line.LineId).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Paso satırı kimliği reçete içinde tekil olmalıdır.", nameof(lines));
        }

        for (var index = 1; index < materialized.Length; index++)
        {
            var prior = materialized[index - 1];
            var current = materialized[index];
            if (prior.OutputThicknessMm != current.InputThicknessMm
                || prior.OutputWidthMm != current.InputWidthMm)
            {
                throw new ArgumentException(
                    "Ardışık paso çıkış ve giriş kalınlık/en ölçüleri tam eşleşmelidir.", nameof(lines));
            }
        }

        PassRecipeId = passRecipeId;
        Version = version;
        _lines = Array.AsReadOnly(materialized);
    }

    public Guid PassRecipeId { get; }
    public string Version { get; }
    public IReadOnlyList<ColdRollingPassLine> Lines => _lines;
}
