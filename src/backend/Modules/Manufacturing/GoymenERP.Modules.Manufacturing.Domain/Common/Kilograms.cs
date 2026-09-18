namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// A measured mass in kilograms, exactly as recorded. The domain never rounds it: the unit
/// profile, precision and rounding stage are owned elsewhere (plan v2 §7.3, Ö2) and are not
/// chosen here.
/// </summary>
public readonly record struct Kilograms
{
    public Kilograms(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Kütle negatif olamaz.");
        }

        Value = value;
    }

    public static Kilograms Zero { get; } = new(0m);

    public decimal Value { get; }

    public static Kilograms operator +(Kilograms left, Kilograms right) => new(left.Value + right.Value);

    public override string ToString() => $"{Value} kg";
}
