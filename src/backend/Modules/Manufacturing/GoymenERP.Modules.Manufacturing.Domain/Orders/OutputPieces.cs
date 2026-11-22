using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// AK-7 (URT-D112): the product owner's theoretical kilograms per piece, captured when a counted output is declared, with
/// the source evidence it was computed from (card, formula, dimension/weight-table, policy and unit-profile versions). It
/// is a snapshot: a later card edit does not move it. Only a positive figure with at most <see cref="Scale"/> decimals
/// and a named source is accepted; nothing is rounded here.
/// </summary>
public sealed record OutputTheoreticalKilogramsPerPiece
{
    /// <summary>The stored decimals of the figure: the Product calculator's own coefficient guard (at most 12).</summary>
    public const int Scale = 12;

    /// <summary>The longest stored source evidence text.</summary>
    public const int SourceVersionMaximumLength = 512;

    public OutputTheoreticalKilogramsPerPiece(decimal kilogramsPerPiece, string sourceVersion)
    {
        if (kilogramsPerPiece <= 0m || decimal.Round(kilogramsPerPiece, Scale) != kilogramsPerPiece)
        {
            throw new ArgumentOutOfRangeException(nameof(kilogramsPerPiece), kilogramsPerPiece,
                $"Teorik kg/adet pozitif olmalı ve en fazla {Scale} ondalık taşımalıdır.");
        }

        var text = sourceVersion?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > SourceVersionMaximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException("Teorik kg/adet kaynağı adlandırılmalıdır.", nameof(sourceVersion));
        }

        KilogramsPerPiece = kilogramsPerPiece;
        SourceVersion = text;
    }

    public decimal KilogramsPerPiece { get; }

    public string SourceVersion { get; }
}

/// <summary>
/// AK-7 (URT-D112): the piece side of a counted output declaration — the declared piece count (at least one) and the
/// theoretical kilograms per piece captured with it. The weighed kilograms stay the declaration's mass; cost stays per kg.
/// </summary>
public sealed record OutputPieces
{
    public OutputPieces(PieceCount count, OutputTheoreticalKilogramsPerPiece theoretical)
    {
        ArgumentNullException.ThrowIfNull(theoretical);
        Count = count.RequirePositive(nameof(count));
        Theoretical = theoretical;
    }

    public PieceCount Count { get; }

    public OutputTheoreticalKilogramsPerPiece Theoretical { get; }
}
