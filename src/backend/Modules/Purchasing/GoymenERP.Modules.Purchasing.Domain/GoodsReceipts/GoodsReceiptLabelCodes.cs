namespace GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

/// <summary>Where a bulk receipt label's code (our code) came from (KLN-D41).</summary>
public enum GoodsReceiptLabelCodeSource
{
    /// <summary>The supplier's label number became our code.</summary>
    SupplierLabelNumber = 1,

    /// <summary>The user entered the code (for example the confirmed suffix suggestion on a conflict).</summary>
    Entered = 2,

    /// <summary>No supplier number: the system generated the code from the Ayarlar format.</summary>
    Generated = 3,
}

/// <summary>
/// ALC-S16 (KLN-D41): label code formatting rules. The formats themselves (the conflict suffix and the generated code) come
/// from Ayarlar; this type only validates and applies them. A format holds exactly one <see cref="NumberPlaceholder"/>.
/// </summary>
public static class GoodsReceiptLabelCodes
{
    /// <summary>The single placeholder a format must hold; it is replaced by a positive integer.</summary>
    public const string NumberPlaceholder = "{n}";

    /// <summary>The stored bound of a label code (the Inventory label code bound).</summary>
    public const int MaximumCodeLength = 64;

    /// <summary>The bound of an Ayarlar format text.</summary>
    public const int MaximumFormatLength = 32;

    /// <summary>The first suffix number offered on a conflict: the taken code itself is the first one.</summary>
    public const int FirstSuffixNumber = 2;

    /// <summary>How many suffix numbers are tried before the suggestion gives up (fail-closed, never an endless scan).</summary>
    public const int MaximumSuffixAttempts = 999;

    /// <summary>True when the text is a usable Ayarlar format: bounded, no control or blank-only text, exactly one placeholder.</summary>
    public static bool IsValidFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) || format.Length > MaximumFormatLength || format.Any(char.IsControl))
        {
            return false;
        }

        var first = format.IndexOf(NumberPlaceholder, StringComparison.Ordinal);
        return first >= 0 && format.IndexOf(NumberPlaceholder, first + NumberPlaceholder.Length, StringComparison.Ordinal) < 0;
    }

    /// <summary>Applies a valid format to a positive number.</summary>
    /// <exception cref="ArgumentException">The format is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The number is not positive.</exception>
    public static string Format(string format, long number)
    {
        if (!IsValidFormat(format))
        {
            throw new ArgumentException("Etiket kodu biçimi geçerli değil.", nameof(format));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return format.Replace(NumberPlaceholder, number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// KLN-D41: the first free code made of the taken base code followed by the Ayarlar suffix format, starting at
    /// <see cref="FirstSuffixNumber"/>. Null when the suffix format is not valid, the result would exceed
    /// <see cref="MaximumCodeLength"/> or no free number is found within <see cref="MaximumSuffixAttempts"/>.
    /// The suggestion is never applied silently; the user confirms it.
    /// </summary>
    public static string? SuggestSuffix(string baseCode, string? suffixFormat, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(baseCode);
        ArgumentNullException.ThrowIfNull(isTaken);
        if (suffixFormat is null || !IsValidFormat(suffixFormat))
        {
            return null;
        }

        for (var number = FirstSuffixNumber; number < FirstSuffixNumber + MaximumSuffixAttempts; number++)
        {
            var candidate = baseCode + Format(suffixFormat, number);
            if (candidate.Length > MaximumCodeLength)
            {
                return null;
            }

            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
