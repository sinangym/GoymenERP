namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// AK-7 (URT-D112, KLN-D119): a counted number of pieces. A piece count is a whole number that is never negative; a
/// declaration counts at least one piece (<see cref="RequirePositive"/>), a correction may take back none.
/// </summary>
public readonly record struct PieceCount
{
    public PieceCount(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Adet negatif olamaz.");
        }

        Value = value;
    }

    public int Value { get; }

    /// <summary>The same count, refused when it is zero (a declared output counts at least one piece).</summary>
    public PieceCount RequirePositive(string parameterName) =>
        Value > 0 ? this : throw new ArgumentException("Adet sıfırdan büyük olmalıdır.", parameterName);

    public override string ToString() => $"{Value} adet";
}
