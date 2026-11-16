using System.Globalization;

namespace GoymenERP.Modules.Manufacturing.Domain.Lineage;

/// <summary>Where an output label's own code came from (URT-D62).</summary>
public enum ProductionOutputLabelCodeSource
{
    /// <summary>The operator entered the code on the output declaration.</summary>
    Entered = 1,

    /// <summary>The system generated the next number of the company's output label format from Ayarlar.</summary>
    Generated = 2,
}

/// <summary>
/// URT-S21 (URT-D62): the formatting rules of an output label's own number. The output carries an independent number;
/// the input label it came from is kept only as a lineage edge and never enters the output's code. The format comes from
/// Ayarlar and holds exactly one <see cref="NumberPlaceholder"/>, the only part the system fills in: no other placeholder
/// exists, so no input label number, order or product text can be put into a generated code. This type only validates
/// and applies a format; it chooses none.
/// </summary>
public static class ProductionOutputLabelCodes
{
    /// <summary>The single placeholder a format must hold; it is replaced by a positive integer.</summary>
    public const string NumberPlaceholder = "{n}";

    /// <summary>The stored bound of a label code (the Inventory label code bound).</summary>
    public const int MaximumCodeLength = 64;

    /// <summary>The bound of an Ayarlar format text.</summary>
    public const int MaximumFormatLength = 32;

    /// <summary>
    /// How many numbers are tried when the next generated code is already taken before the generation gives up
    /// (fail-closed, never an endless scan).
    /// </summary>
    public const int MaximumGenerationAttempts = 999;

    /// <summary>True when the text is a usable Ayarlar format: bounded, no control or blank-only text, exactly one placeholder.</summary>
    public static bool IsValidFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Length > MaximumFormatLength || format.Any(char.IsControl))
        {
            return false;
        }

        var first = format.IndexOf(NumberPlaceholder, StringComparison.Ordinal);
        return first >= 0
               && format.IndexOf(NumberPlaceholder, first + NumberPlaceholder.Length, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// URT-SC36 (URT-D98): true when an operator may enter an output label code himself — only while no output label
    /// number format is defined in Ayarlar. Once a format is defined (even an unusable one) the code comes only from the
    /// format and an entered code is refused; the uniqueness of every code is kept either way.
    /// </summary>
    public static bool AcceptsEnteredCode(string? format) => string.IsNullOrWhiteSpace(format);

    /// <summary>True when the text is a usable label code: not blank, no control character, within the stored bound.</summary>
    public static bool IsValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length <= MaximumCodeLength && !code.Any(char.IsControl);

    /// <summary>Applies a valid format to a positive number.</summary>
    /// <exception cref="ArgumentException">The format is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The number is not positive.</exception>
    public static string Format(string format, long number)
    {
        if (!IsValidFormat(format))
        {
            throw new ArgumentException("Çıktı etiket numarası biçimi geçerli değil.", nameof(format));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return format.Replace(NumberPlaceholder, number.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
