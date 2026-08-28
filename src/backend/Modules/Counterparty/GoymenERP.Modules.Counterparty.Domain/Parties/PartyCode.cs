using System.Globalization;

namespace GoymenERP.Modules.Counterparty.Domain.Parties;

public readonly record struct PartyCode
{
    private const string Prefix = "CAR-";

    private PartyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PartyCode FromSequence(long sequence)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "Cari kodu sıra numarası sıfırdan büyük olmalıdır.");
        }

        return new PartyCode($"{Prefix}{sequence:D6}");
    }

    public static PartyCode Parse(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)
            || !normalized.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Cari kodu CAR- ön ekiyle başlamalıdır.",
                nameof(value));
        }

        var digits = normalized[Prefix.Length..];
        if (digits.Length < 6
            || digits.Any(character => !char.IsAsciiDigit(character))
            || !long.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var sequence)
            || sequence <= 0)
        {
            throw new ArgumentException(
                "Cari kodu CAR-000001 biçiminde pozitif bir sıra numarası taşımalıdır.",
                nameof(value));
        }

        var canonical = FromSequence(sequence);
        if (!string.Equals(normalized, canonical.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Cari kodu tek kanonik CAR-000001 biçiminde olmalıdır.",
                nameof(value));
        }

        return canonical;
    }

    public override string ToString() => Value;
}
