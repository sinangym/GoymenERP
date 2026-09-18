namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// A measured mass in kilograms. Ö2: kilograms carry at most three decimals and digits are never
/// cut silently, so a fourth significant decimal is REFUSED, not rounded; trailing zeros are not
/// digits ("12.5000" is 12.5 kg). The domain rounds nothing.
/// </summary>
public readonly record struct Kilograms
{
    public const int Scale = 3;

    public Kilograms(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Kütle negatif olamaz.");
        }

        if (decimal.Round(value, Scale) != value)
        {
            throw new ArgumentException($"Kilogram en fazla {Scale} ondalık taşıyabilir.", nameof(value));
        }

        Value = value;
    }

    public static Kilograms Zero { get; } = new(0m);

    public decimal Value { get; }

    public static Kilograms operator +(Kilograms left, Kilograms right) => new(left.Value + right.Value);

    public override string ToString() => $"{Value} kg";
}
