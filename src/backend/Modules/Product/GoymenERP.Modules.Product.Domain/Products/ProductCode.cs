using System.Globalization;

namespace GoymenERP.Modules.Product.Domain.Products;

/// <summary>A server-allocated group prefix and sequence (DEC-PRD-004).</summary>
public readonly record struct ProductCode
{
    public const int MaximumPrefixLength = 32;
    private ProductCode(string value) => Value = value;

    public string Value { get; }

    public static ProductCode FromSequence(string groupPrefix, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupPrefix);
        var prefix = groupPrefix.Trim().ToUpperInvariant();
        if (prefix.Length > MaximumPrefixLength || prefix.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("Ürün grubu ön eki yalnız harf ve rakam içerebilir.", nameof(groupPrefix));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return new ProductCode(string.Concat(prefix, "-", sequence.ToString(CultureInfo.InvariantCulture)));
    }

    public static ProductCode Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var separator = value.LastIndexOf('-');
        if (separator <= 0 || !long.TryParse(value[(separator + 1)..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var sequence))
        {
            throw new ArgumentException("Ürün kodu geçersiz.", nameof(value));
        }

        var code = FromSequence(value[..separator], sequence);
        if (!string.Equals(code.Value, value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Ürün kodu kanonik biçimde olmalıdır.", nameof(value));
        }

        return code;
    }

    public override string ToString() => Value;
}
