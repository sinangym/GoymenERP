using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The values of one bank CSV mapping version as codes and numbers, the shape both a request and a stored row reduce to, so
/// the administration and the provider apply the very same rules (<see cref="BankCsvMappingRulesV1.Refusal"/>).
/// </summary>
public sealed record BankCsvMappingValuesV1(
    string? State,
    string? Delimiter,
    int? SkipLeadingRows,
    bool? HasHeaderRow,
    string? DecimalSeparator,
    string? ThousandsSeparator,
    string? DateFormat,
    string? AmountLayout,
    string? SignedAmountPositiveMeans,
    int? TransactionDateColumn,
    int? ValueDateColumn,
    int? DescriptionColumn,
    int? AmountColumn,
    int? InflowColumn,
    int? OutflowColumn,
    int? BalanceColumn,
    int? ReferenceColumn,
    int? CurrencyColumn);

/// <summary>
/// FNS-D52: the deterministic rules of a bank CSV column mapping, shared by the administration, the provider and the tests.
/// Every check is closed and exact: a code is accepted as written (case-sensitive) or refused, never trimmed, folded or
/// replaced by a default. The refusal texts are Turkish because the administration returns them to a person.
/// </summary>
public static class BankCsvMappingRulesV1
{
    /// <summary>
    /// A bank key: 1…<see cref="BankCsvMappingInputLimitsV1.BankKeyMaximumLength"/> characters of ASCII <c>A-Z</c>,
    /// <c>0-9</c>, <c>_</c> and <c>-</c>, the first a letter or digit. Exact: a lower-case or padded key is not a key.
    /// </summary>
    public static bool IsBankKey(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.Length <= BankCsvMappingInputLimitsV1.BankKeyMaximumLength
        && (char.IsAsciiLetterUpper(key[0]) || char.IsAsciiDigit(key[0]))
        && key.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)
                                || character == '_' || character == '-');

    /// <summary>
    /// A display name: trimmed, 1…<see cref="BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength"/> characters, no
    /// control characters. <paramref name="normalized"/> is the trimmed text.
    /// </summary>
    public static bool TryNormalizeDisplayName(string? name, out string normalized)
    {
        normalized = name?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength
               && !normalized.Any(char.IsControl);
    }

    /// <summary>A state code from <see cref="BankCsvMappingStatesV1.All"/>.</summary>
    public static bool IsState(string? code) =>
        code is BankCsvMappingStatesV1.Active or BankCsvMappingStatesV1.Inactive;

    /// <summary>A delimiter code from <see cref="BankCsvDelimitersV1.All"/> and its character.</summary>
    public static bool TryParseDelimiter(string? code, out char delimiter)
    {
        delimiter = code switch
        {
            BankCsvDelimitersV1.Semicolon => ';',
            BankCsvDelimitersV1.Comma => ',',
            BankCsvDelimitersV1.Tab => '\t',
            _ => '\0',
        };
        return delimiter != '\0';
    }

    /// <summary>A decimal separator code from <see cref="BankCsvDecimalSeparatorsV1.All"/> and its character.</summary>
    public static bool TryParseDecimalSeparator(string? code, out char separator)
    {
        separator = code switch
        {
            BankCsvDecimalSeparatorsV1.Comma => ',',
            BankCsvDecimalSeparatorsV1.Period => '.',
            _ => '\0',
        };
        return separator != '\0';
    }

    /// <summary>
    /// A thousands separator code from <see cref="BankCsvThousandsSeparatorsV1.All"/> and its character (null for
    /// <see cref="BankCsvThousandsSeparatorsV1.None"/>).
    /// </summary>
    public static bool TryParseThousandsSeparator(string? code, out char? separator)
    {
        switch (code)
        {
            case BankCsvThousandsSeparatorsV1.None:
                separator = null;
                return true;
            case BankCsvThousandsSeparatorsV1.Period:
                separator = '.';
                return true;
            case BankCsvThousandsSeparatorsV1.Comma:
                separator = ',';
                return true;
            case BankCsvThousandsSeparatorsV1.Space:
                separator = ' ';
                return true;
            default:
                separator = null;
                return false;
        }
    }

    /// <summary>A date pattern from <see cref="BankCsvDateFormatsV1.All"/> (exact, ordinal).</summary>
    public static bool IsDateFormat(string? code) =>
        code is not null && BankCsvDateFormatsV1.All.Contains(code, StringComparer.Ordinal);

    /// <summary>An amount layout code from <see cref="BankCsvAmountLayoutsV1.All"/>.</summary>
    public static bool TryParseAmountLayout(string? code, out SettingsBankCsvAmountLayoutV1 layout)
    {
        layout = code switch
        {
            BankCsvAmountLayoutsV1.SignedAmount => SettingsBankCsvAmountLayoutV1.SignedAmount,
            BankCsvAmountLayoutsV1.InflowOutflowColumns => SettingsBankCsvAmountLayoutV1.InflowOutflowColumns,
            _ => default,
        };
        return layout != default;
    }

    /// <summary>A positive-amount meaning code from <see cref="BankCsvSignedAmountDirectionsV1.All"/>.</summary>
    public static bool TryParseSignedAmountDirection(string? code, out SettingsBankCsvSignedAmountDirectionV1 direction)
    {
        direction = code switch
        {
            BankCsvSignedAmountDirectionsV1.Inflow => SettingsBankCsvSignedAmountDirectionV1.Inflow,
            BankCsvSignedAmountDirectionsV1.Outflow => SettingsBankCsvSignedAmountDirectionV1.Outflow,
            _ => default,
        };
        return direction != default;
    }

    /// <summary>A 1-based column number within 1…<see cref="BankCsvMappingInputLimitsV1.MaximumColumnNumber"/>.</summary>
    public static bool IsColumn(int? column) => column is >= 1 and <= BankCsvMappingInputLimitsV1.MaximumColumnNumber;

    /// <summary>A skipped leading row count within 0…<see cref="BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows"/>.</summary>
    public static bool IsSkipLeadingRows(int? rows) => rows is >= 0 and <= BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows;

    /// <summary>
    /// The first rule <paramref name="values"/> breaks, as a Turkish detail for the person, or null when the mapping is
    /// complete and consistent: every code from its closed list, the thousands separator different from the decimal one, the
    /// layout's own columns present and the other layout's absent, every column in range and no column used twice.
    /// </summary>
    public static string? Refusal(BankCsvMappingValuesV1 values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!IsState(values.State))
        {
            return "Durum tanımlı listeden seçilmelidir: " + string.Join(", ", BankCsvMappingStatesV1.All) + ".";
        }

        if (!TryParseDelimiter(values.Delimiter, out _))
        {
            return "Alan ayracı tanımlı listeden seçilmelidir: " + string.Join(", ", BankCsvDelimitersV1.All) + ".";
        }

        if (!IsSkipLeadingRows(values.SkipLeadingRows))
        {
            return "Atlanacak baştaki satır sayısı 0 ile "
                   + BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows.ToString(CultureInfo.InvariantCulture)
                   + " arasında tam sayı olmalıdır.";
        }

        if (values.HasHeaderRow is null)
        {
            return "Başlık satırı olup olmadığı belirtilmelidir.";
        }

        if (!TryParseDecimalSeparator(values.DecimalSeparator, out var decimalSeparator)
            || !TryParseThousandsSeparator(values.ThousandsSeparator, out var thousandsSeparator))
        {
            return "Ondalık ayracı (" + string.Join(", ", BankCsvDecimalSeparatorsV1.All) + ") ve binlik ayracı ("
                   + string.Join(", ", BankCsvThousandsSeparatorsV1.All) + ") tanımlı listeden seçilmelidir.";
        }

        if (thousandsSeparator == decimalSeparator)
        {
            return "Binlik ayracı ondalık ayracıyla aynı olamaz.";
        }

        if (!IsDateFormat(values.DateFormat))
        {
            return "Tarih biçimi tanımlı listeden seçilmelidir: " + string.Join(", ", BankCsvDateFormatsV1.All) + ".";
        }

        if (!TryParseAmountLayout(values.AmountLayout, out var layout))
        {
            return "Tutar düzeni tanımlı listeden seçilmelidir: " + string.Join(", ", BankCsvAmountLayoutsV1.All) + ".";
        }

        if (layout == SettingsBankCsvAmountLayoutV1.SignedAmount)
        {
            if (!IsColumn(values.AmountColumn) || values.InflowColumn is not null || values.OutflowColumn is not null)
            {
                return "İşaretli tutar düzeninde tutar sütunu zorunludur; giriş ve çıkış sütunları boş bırakılır.";
            }

            if (!TryParseSignedAmountDirection(values.SignedAmountPositiveMeans, out _))
            {
                return "İşaretli tutar düzeninde pozitif tutarın anlamı seçilmelidir: "
                       + string.Join(", ", BankCsvSignedAmountDirectionsV1.All) + ".";
            }
        }
        else if (values.AmountColumn is not null || !IsColumn(values.InflowColumn) || !IsColumn(values.OutflowColumn)
                 || values.SignedAmountPositiveMeans is not null)
        {
            return "Giriş/çıkış sütunlu düzende giriş ve çıkış sütunları zorunludur; tutar sütunu ve pozitif tutar anlamı boş bırakılır.";
        }

        if (!IsColumn(values.TransactionDateColumn) || !IsColumn(values.DescriptionColumn))
        {
            return "İşlem tarihi ve açıklama sütunları zorunludur.";
        }

        int?[] optional = [values.ValueDateColumn, values.BalanceColumn, values.ReferenceColumn, values.CurrencyColumn];
        if (optional.Any(column => column is not null && !IsColumn(column)))
        {
            return "Sütun numaraları 1 ile "
                   + BankCsvMappingInputLimitsV1.MaximumColumnNumber.ToString(CultureInfo.InvariantCulture)
                   + " arasında olmalıdır.";
        }

        var used = Columns(values);
        return used.Count != used.Distinct().Count() ? "Bir sütun numarası birden fazla alana atanamaz." : null;
    }

    /// <summary>The column numbers a mapping uses (nulls left out), in field order.</summary>
    public static IReadOnlyList<int> Columns(BankCsvMappingValuesV1 values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int?[] all =
        [
            values.TransactionDateColumn,
            values.ValueDateColumn,
            values.DescriptionColumn,
            values.AmountColumn,
            values.InflowColumn,
            values.OutflowColumn,
            values.BalanceColumn,
            values.ReferenceColumn,
            values.CurrencyColumn,
        ];
        return all.Where(column => column is not null).Select(column => column!.Value).ToArray();
    }
}
