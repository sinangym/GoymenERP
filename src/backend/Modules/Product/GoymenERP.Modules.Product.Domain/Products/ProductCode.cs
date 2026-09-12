using System.Globalization;

namespace GoymenERP.Modules.Product.Domain.Products;

/// <summary>A server-allocated group prefix and sequence (DEC-PRD-004).</summary>
public readonly record struct ProductCode
{
    private ProductCode(string value) => Value = value;

    public string Value { get; }

    public static ProductCode FromSequence(string groupPrefix, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupPrefix);
        var prefix = groupPrefix.Trim().ToUpperInvariant();
        if (prefix.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("Ürün grubu ön eki yalnız harf ve rakam içerebilir.", nameof(groupPrefix));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return new ProductCode(string.Concat(prefix, "-", sequence.ToString(CultureInfo.InvariantCulture)));
    }

    public override string ToString() => Value;
}
