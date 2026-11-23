using System.Globalization;
using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>One statement line as read from the file: exact canonical amount text, no business reading.</summary>
internal sealed record FinanceParsedStatementLine(
    int LineNumber,
    DateOnly TransactionOn,
    DateOnly? ValueOn,
    FinanceBankStatementDirectionV1 Direction,
    string Amount,
    string? Balance,
    string? CurrencyText,
    string? Reference,
    string Description);

/// <summary>
/// The parse answer. A file with any issue is refused as a whole: nothing of it is stored (fail-closed, deterministic).
/// <c>AccountIdentifiers</c> are the MT940 <c>:25:</c> values (normalized), empty for CSV.
/// </summary>
internal sealed record FinanceStatementParseResult(
    IReadOnlyList<FinanceParsedStatementLine> Lines,
    IReadOnlyList<FinanceBankStatementIssueV1> Issues,
    IReadOnlyList<string> AccountIdentifiers)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// FNS-D52: reads a CSV statement with the bank's Settings column mapping (exactly; no fallback, no guessed column) and a
/// SWIFT MT940 statement. Every value is parsed strictly; a value that does not fit is an issue, never a default.
/// </summary>
internal static partial class FinanceBankStatementParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static FinanceStatementParseResult ParseCsv(ReadOnlySpan<byte> content, SettingsBankCsvMappingV1 mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var issues = new Issues();
        if (!TryDecode(content, issues, out var text)) return issues.Result();
        var records = new List<(int LineNumber, string[] Cells)>();
        if (!TryTokenize(text, mapping.Delimiter, issues, records)) return issues.Result();

        var dataStart = mapping.SkipLeadingRows + (mapping.HasHeaderRow ? 1 : 0);
        var lines = new List<FinanceParsedStatementLine>();
        foreach (var (lineNumber, cells) in records.Skip(dataStart))
        {
            if (cells.All(cell => cell.Trim().Length == 0)) continue;
            if (lines.Count >= FinanceBankStatementLimitsV1.MaximumLines)
            {
                issues.Add(lineNumber, "file", "line_limit_exceeded");
                break;
            }

            var line = ReadCsvLine(lineNumber, cells, mapping, issues);
            if (line is not null) lines.Add(line);
            if (issues.Full) break;
        }

        return issues.Result(lines, []);
    }

    private static FinanceParsedStatementLine? ReadCsvLine(int lineNumber, string[] cells, SettingsBankCsvMappingV1 mapping,
        Issues issues)
    {
        var before = issues.Count;
        var columns = mapping.Columns;
        string? Cell(int? column) => column is { } index && index <= cells.Length ? cells[index - 1].Trim() : null;
        bool Present(int? column) => column is null || column.Value <= cells.Length;

        if (!Present(columns.TransactionDate) || !Present(columns.ValueDate) || !Present(columns.Description)
            || !Present(columns.Amount) || !Present(columns.Inflow) || !Present(columns.Outflow)
            || !Present(columns.Balance) || !Present(columns.Reference) || !Present(columns.Currency))
        {
            issues.Add(lineNumber, "row", "column_missing");
            return null;
        }

        var transactionOn = Date(Cell(columns.TransactionDate), mapping.DateFormat);
        if (transactionOn is null) issues.Add(lineNumber, "transactionDate", "date_invalid");

        DateOnly? valueOn = null;
        var valueText = Cell(columns.ValueDate);
        if (!string.IsNullOrEmpty(valueText))
        {
            valueOn = Date(valueText, mapping.DateFormat);
            if (valueOn is null) issues.Add(lineNumber, "valueDate", "date_invalid");
        }

        FinanceBankStatementDirectionV1 direction = default;
        string? amount = null;
        if (mapping.AmountLayout == SettingsBankCsvAmountLayoutV1.SignedAmount)
        {
            if (!TryAmount(Cell(columns.Amount), mapping, allowSign: true, out var negative, out amount) || amount is null
                || IsZero(amount) || mapping.SignedAmountPositiveMeans is null)
            {
                issues.Add(lineNumber, "amount", "amount_invalid");
            }
            else
            {
                var positiveIsInflow = mapping.SignedAmountPositiveMeans == SettingsBankCsvSignedAmountDirectionV1.Inflow;
                direction = negative ^ positiveIsInflow ? FinanceBankStatementDirectionV1.Inflow : FinanceBankStatementDirectionV1.Outflow;
            }
        }
        else
        {
            var inflowOk = TryAmount(Cell(columns.Inflow), mapping, allowSign: false, out _, out var inflow);
            var outflowOk = TryAmount(Cell(columns.Outflow), mapping, allowSign: false, out _, out var outflow);
            var inflowSet = inflow is not null && !IsZero(inflow);
            var outflowSet = outflow is not null && !IsZero(outflow);
            if (!inflowOk || !outflowOk || inflowSet == outflowSet)
            {
                issues.Add(lineNumber, "amount", "amount_invalid");
            }
            else
            {
                direction = inflowSet ? FinanceBankStatementDirectionV1.Inflow : FinanceBankStatementDirectionV1.Outflow;
                amount = inflowSet ? inflow : outflow;
            }
        }

        string? balance = null;
        var balanceText = Cell(columns.Balance);
        if (!string.IsNullOrEmpty(balanceText))
        {
            // A balance may be negative (an overdraft); it is information only and carries its sign.
            if (!TryAmount(balanceText, mapping, allowSign: true, out var negativeBalance, out balance) || balance is null)
                issues.Add(lineNumber, "balance", "amount_invalid");
            else if (negativeBalance && !IsZero(balance)) balance = "-" + balance;
        }

        var currency = Cell(columns.Currency);
        if (string.IsNullOrEmpty(currency)) currency = null;
        else if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) issues.Add(lineNumber, "currency", "currency_invalid");

        var reference = Text(Cell(columns.Reference));
        if (reference is { Length: > FinanceBankStatementLimitsV1.ReferenceMaximumLength })
            issues.Add(lineNumber, "reference", "text_too_long");
        var description = Text(Cell(columns.Description)) ?? string.Empty;
        if (description.Length == 0) issues.Add(lineNumber, "description", "text_required");
        else if (description.Length > FinanceBankStatementLimitsV1.DescriptionMaximumLength)
            issues.Add(lineNumber, "description", "text_too_long");

        return issues.Count != before || transactionOn is null || amount is null
            ? null
            : new FinanceParsedStatementLine(lineNumber, transactionOn.Value, valueOn, direction, amount, balance, currency,
                reference, description);
    }

    /// <summary>
    /// SWIFT MT940 (customer statement): <c>:25:</c> account, <c>:60F/M:</c> opening balance (its currency is every line's),
    /// <c>:61:</c> statement line, <c>:86:</c> information to the account owner (the description). The <c>:61:</c> value
    /// date is the line's value date; its optional entry date (booking) is the transaction date, otherwise the value date.
    /// <c>RC</c>/<c>RD</c> (reversals) keep the money direction they produce (out/in).
    /// </summary>
    public static FinanceStatementParseResult ParseMt940(ReadOnlySpan<byte> content)
    {
        var issues = new Issues();
        if (!TryDecode(content, issues, out var text)) return issues.Result();
        var fields = new List<(int LineNumber, string Tag, string Value)>();
        var physical = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < physical.Length; index++)
        {
            var raw = physical[index];
            var line = raw.TrimEnd();
            var lineNumber = index + 1;
            if (line.Length == 0 || line == "-" || line == "-}" || line.StartsWith('{') && !line.StartsWith("{4:", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("{4:", StringComparison.Ordinal)) line = line[3..];
            if (line.Length == 0) continue;
            if (line.Length > 1 && line[0] == ':' && line.IndexOf(':', 1) is var end and > 1)
            {
                fields.Add((lineNumber, line[1..end], line[(end + 1)..]));
            }
            else if (fields.Count > 0)
            {
                var last = fields[^1];
                fields[^1] = (last.LineNumber, last.Tag, last.Value + "\n" + line);
            }
            else
            {
                issues.Add(lineNumber, "file", "mt940_structure_invalid");
                return issues.Result();
            }
        }

        var accounts = new List<string>();
        var lines = new List<FinanceParsedStatementLine>();
        string? currency = null;
        var openingSeen = false;
        for (var index = 0; index < fields.Count; index++)
        {
            var (lineNumber, tag, value) = fields[index];
            switch (tag)
            {
                case "20":
                    currency = null;
                    openingSeen = false;
                    break;
                case "25":
                    var account = FinanceIban.Normalize(value.Split('/')[0].Trim());
                    if (account.Length == 0) issues.Add(lineNumber, "account", "mt940_account_invalid");
                    else accounts.Add(account);
                    break;
                case "60F" or "60M":
                    currency = BalanceCurrency(value);
                    openingSeen = currency is not null;
                    if (!openingSeen) issues.Add(lineNumber, "openingBalance", "mt940_balance_invalid");
                    break;
                case "61":
                    if (!openingSeen || currency is null)
                    {
                        issues.Add(lineNumber, "statementLine", "mt940_structure_invalid");
                        break;
                    }

                    if (lines.Count >= FinanceBankStatementLimitsV1.MaximumLines)
                    {
                        issues.Add(lineNumber, "file", "line_limit_exceeded");
                        return issues.Result(lines, accounts);
                    }

                    var information = index + 1 < fields.Count && fields[index + 1].Tag == "86" ? fields[index + 1].Value : null;
                    var line = ReadMt940Line(lineNumber, value, information, currency, issues);
                    if (line is not null) lines.Add(line);
                    break;
            }

            if (issues.Full) break;
        }

        if (lines.Count == 0 && issues.Count == 0 && fields.All(field => field.Tag != "20"))
            issues.Add(0, "file", "mt940_structure_invalid");
        return issues.Result(lines, accounts.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static FinanceParsedStatementLine? ReadMt940Line(int lineNumber, string value, string? information,
        string currency, Issues issues)
    {
        var parts = value.Split('\n');
        var head = parts[0];
        var supplementary = parts.Length > 1 ? string.Join(' ', parts.Skip(1).Select(part => part.Trim())) : null;
        if (head.Length < 6 || !TryYyMmDd(head[..6], out var valueOn))
        {
            issues.Add(lineNumber, "valueDate", "date_invalid");
            return null;
        }

        var position = 6;
        var transactionOn = valueOn;
        if (head.Length >= position + 4 && head[position..(position + 4)].All(char.IsAsciiDigit))
        {
            if (!TryEntryDate(valueOn, head[position..(position + 4)], out transactionOn))
            {
                issues.Add(lineNumber, "transactionDate", "date_invalid");
                return null;
            }

            position += 4;
        }

        FinanceBankStatementDirectionV1 direction;
        if (head.AsSpan(position).StartsWith("RC")) { direction = FinanceBankStatementDirectionV1.Outflow; position += 2; }
        else if (head.AsSpan(position).StartsWith("RD")) { direction = FinanceBankStatementDirectionV1.Inflow; position += 2; }
        else if (head.AsSpan(position).StartsWith("C")) { direction = FinanceBankStatementDirectionV1.Inflow; position += 1; }
        else if (head.AsSpan(position).StartsWith("D")) { direction = FinanceBankStatementDirectionV1.Outflow; position += 1; }
        else
        {
            issues.Add(lineNumber, "direction", "mt940_mark_invalid");
            return null;
        }

        // Optional funds code: a letter before the amount's first digit.
        if (position < head.Length && char.IsAsciiLetter(head[position])) position++;
        var amountStart = position;
        while (position < head.Length && (char.IsAsciiDigit(head[position]) || head[position] == ',')) position++;
        var amount = Mt940Amount(head[amountStart..position]);
        if (amount is null || IsZero(amount))
        {
            issues.Add(lineNumber, "amount", "amount_invalid");
            return null;
        }

        // Transaction type: one of N/F/S and three characters.
        if (head.Length < position + 4 || head[position] is not ('N' or 'F' or 'S'))
        {
            issues.Add(lineNumber, "transactionType", "mt940_structure_invalid");
            return null;
        }

        position += 4;
        var references = head[position..];
        var split = references.IndexOf("//", StringComparison.Ordinal);
        var customerReference = (split < 0 ? references : references[..split]).Trim();
        var bankReference = split < 0 ? string.Empty : references[(split + 2)..].Trim();
        var reference = customerReference.Length > 0 && !string.Equals(customerReference, "NONREF", StringComparison.Ordinal)
            ? customerReference
            : bankReference.Length > 0 ? bankReference : null;
        reference = Text(reference);
        if (reference is { Length: > FinanceBankStatementLimitsV1.ReferenceMaximumLength })
        {
            issues.Add(lineNumber, "reference", "text_too_long");
            return null;
        }

        var descriptionParts = new[] { information?.Replace('\n', ' '), supplementary }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var description = Text(string.Join(' ', descriptionParts)) ?? string.Empty;
        if (description.Length == 0) description = reference ?? string.Empty;
        if (description.Length == 0)
        {
            issues.Add(lineNumber, "description", "text_required");
            return null;
        }

        if (description.Length > FinanceBankStatementLimitsV1.DescriptionMaximumLength)
        {
            issues.Add(lineNumber, "description", "text_too_long");
            return null;
        }

        return new FinanceParsedStatementLine(lineNumber, transactionOn, valueOn, direction, amount, null, currency,
            reference, description);
    }

    private static string? BalanceCurrency(string value)
    {
        // C|D, YYMMDD, three-letter currency, amount.
        if (value.Length < 11 || value[0] is not ('C' or 'D') || !TryYyMmDd(value[1..7], out _)) return null;
        var currency = value[7..10];
        return currency.All(char.IsAsciiLetterUpper) && Mt940Amount(value[10..].Trim()) is not null ? currency : null;
    }

    private static bool TryYyMmDd(string text, out DateOnly date)
    {
        date = default;
        if (text.Length != 6 || !text.All(char.IsAsciiDigit)) return false;
        // SWIFT two-digit years: this importer reads them as 2000–2099 (no statement before 2000 is in scope).
        return DateOnly.TryParseExact("20" + text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryEntryDate(DateOnly valueOn, string monthDay, out DateOnly entryOn)
    {
        entryOn = default;
        var month = int.Parse(monthDay[..2], CultureInfo.InvariantCulture);
        var day = int.Parse(monthDay[2..], CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return false;
        // The entry date has no year: it is the year of the value date, moved by one when the months straddle a year end.
        var year = valueOn.Year + (month - valueOn.Month > 6 ? -1 : valueOn.Month - month > 6 ? 1 : 0);
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        entryOn = new DateOnly(year, month, day);
        return true;
    }

    private static string? Mt940Amount(string text)
    {
        // SWIFT amounts: digits, exactly one comma as the decimal mark (it may end the amount), no group separator.
        if (text.Length == 0 || text.Count(c => c == ',') != 1 || text[0] == ',' || !text.All(c => char.IsAsciiDigit(c) || c == ','))
            return null;
        var parts = text.Split(',');
        return Canonical(parts[0], parts[1]);
    }

    private static DateOnly? Date(string? text, string format)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? DateOnly.FromDateTime(value)
            : null;
    }

    /// <summary>
    /// Reads an amount with the mapping's separators. Empty text is "no amount" (true, null). The digit group separator is
    /// accepted only between groups of exactly three digits; a sign is accepted only when <paramref name="allowSign"/>.
    /// </summary>
    private static bool TryAmount(string? text, SettingsBankCsvMappingV1 mapping, bool allowSign, out bool negative,
        out string? amount)
    {
        negative = false;
        amount = null;
        if (string.IsNullOrEmpty(text)) return true;
        var value = text;
        if (value[0] is '-' or '+')
        {
            if (!allowSign) return false;
            negative = value[0] == '-';
            value = value[1..];
        }

        var decimalAt = value.IndexOf(mapping.DecimalSeparator);
        if (decimalAt >= 0 && value.IndexOf(mapping.DecimalSeparator, decimalAt + 1) >= 0) return false;
        var integral = decimalAt < 0 ? value : value[..decimalAt];
        var fraction = decimalAt < 0 ? string.Empty : value[(decimalAt + 1)..];
        if (decimalAt >= 0 && fraction.Length == 0) return false;
        if (mapping.ThousandsSeparator is { } group && integral.Contains(group))
        {
            var groups = integral.Split(group);
            if (groups[0].Length is < 1 or > 3 || groups.Skip(1).Any(part => part.Length != 3)) return false;
            integral = string.Concat(groups);
        }

        amount = Canonical(integral, fraction);
        return amount is not null;
    }

    private static string? Canonical(string integral, string fraction)
    {
        if (integral.Length == 0 || !integral.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)) return null;
        integral = integral.TrimStart('0');
        if (integral.Length == 0) integral = "0";
        return fraction.Length == 0 ? integral : integral + "." + fraction;
    }

    private static bool IsZero(string amount) => amount.All(c => c is '0' or '.');

    /// <summary>
    /// Text cells are stored as read (whitespace collapsed); a leading spreadsheet formula character (<c>=</c>, <c>+</c>,
    /// <c>-</c>, <c>@</c>) is neutralized with a <c>'</c> prefix (the Settings mapping leaves this to the importer).
    /// </summary>
    private static string? Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed[0] is '=' or '+' or '-' or '@' ? "'" + collapsed : collapsed;
    }

    private static bool TryDecode(ReadOnlySpan<byte> content, Issues issues, out string text)
    {
        text = string.Empty;
        if (content.IsEmpty)
        {
            issues.Add(0, "file", "file_empty");
            return false;
        }

        if (content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) content = content[3..];
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            issues.Add(0, "file", "utf8_invalid");
            return false;
        }

        if (text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n')))
        {
            issues.Add(0, "file", "control_character");
            return false;
        }

        return true;
    }

    private static bool TryTokenize(string text, char delimiter, Issues issues, List<(int, string[])> records)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (index == lines.Length - 1 && line.Length == 0) break;
            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            var afterQuote = false;
            for (var position = 0; position < line.Length; position++)
            {
                var character = line[position];
                if (quoted)
                {
                    if (character != '"') cell.Append(character);
                    else if (position + 1 < line.Length && line[position + 1] == '"') { cell.Append('"'); position++; }
                    else { quoted = false; afterQuote = true; }
                }
                else if (character == delimiter) { cells.Add(cell.ToString()); cell.Clear(); afterQuote = false; }
                else if (character == '"' && cell.Length == 0 && !afterQuote) quoted = true;
                else if (afterQuote || character == '"')
                {
                    issues.Add(index + 1, "row", "csv_quote_invalid");
                    return false;
                }
                else cell.Append(character);
            }

            if (quoted)
            {
                // A quoted field never spans physical lines here: a statement line is one physical line.
                issues.Add(index + 1, "row", "csv_quote_invalid");
                return false;
            }

            cells.Add(cell.ToString());
            records.Add((index + 1, cells.ToArray()));
        }

        return true;
    }

    private sealed class Issues
    {
        private readonly List<FinanceBankStatementIssueV1> _items = [];
        public int Count => _items.Count;
        public bool Full => _items.Count >= FinanceBankStatementLimitsV1.MaximumIssues;

        public void Add(int lineNumber, string field, string code)
        {
            if (!Full) _items.Add(new FinanceBankStatementIssueV1(lineNumber, field, code));
        }

        public FinanceStatementParseResult Result() => new([], _items.ToArray(), []);

        public FinanceStatementParseResult Result(IReadOnlyList<FinanceParsedStatementLine> lines,
            IReadOnlyList<string> accounts) =>
            _items.Count == 0 ? new(lines, [], accounts) : new([], _items.ToArray(), accounts);
    }
}
