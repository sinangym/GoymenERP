using System.Globalization;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Named limits of one admission (nothing is a hidden constant).</summary>
public static class AccountingSourceDocumentLimitsV1
{
    public const int MaximumLines = 500;
    public const int MaximumIdempotencyKeyLength = 128;
    public const int MaximumActorIdLength = 128;
    public const int MaximumCorrelationIdLength = 128;

    /// <summary>An amount fits decimal(38,18) with room to sum <see cref="MaximumLines"/> lines.</summary>
    public const int MaximumIntegerDigits = 18;

    public const int MaximumScale = 8;
}

/// <summary>Exact decimal text of the port: dot separator, no sign, no grouping, bounded by <see cref="AccountingSourceDocumentLimitsV1"/>.</summary>
public static class AccountingSourceDocumentAmountsV1
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (integerPart.Length is 0 or > AccountingSourceDocumentLimitsV1.MaximumIntegerDigits
            || (integerPart.Length > 1 && integerPart[0] == '0')
            || !integerPart.All(char.IsAsciiDigit)
            || (dot >= 0 && (fraction.Length is 0 || fraction.Length > AccountingSourceDocumentLimitsV1.MaximumScale
                || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>One voucher line still waiting for its account.</summary>
internal sealed record AccountingSourceDocumentPlannedLineV1(
    string MappingKey,
    int Side,
    decimal Amount,
    int? SourceLineNumber,
    string? ItemType,
    string? LinePurpose,
    bool? IsExport,
    string? VatRateCode);

internal sealed record AccountingSourceDocumentPlanV1(
    string? RefusalReason,
    IReadOnlyList<AccountingSourceDocumentPlannedLineV1> Lines)
{
    public static AccountingSourceDocumentPlanV1 Refuse(string reason) => new(reason, []);
}

/// <summary>
/// Pure content check and voucher plan (KLN-D6, D12). Accounting recomputes nothing: every amount is the producer's; it
/// checks that they agree and plans the voucher: debit receivable = payable (net + VAT − withholding), credit revenue per
/// invoice line = net, credit output VAT per VAT-rate group = Σ(VAT − withholding). No threshold is applied (the
/// 12,000 TL tevkifat threshold is the producer's configuration); a withholding line is not written (its counter account is open).
/// </summary>
internal static class AccountingSourceDocumentVoucherPlanV1
{
    public static AccountingSourceDocumentPlanV1 Plan(AccountingSourceDocumentAdmissionRequestV1 request)
    {
        if (request.DocumentDate == default || request.PartyId == Guid.Empty || request.CurrencyId == Guid.Empty
            || request.SourceActorId == Guid.Empty
            || (request.DueDate is { } due && due < request.DocumentDate))
        {
            return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.DocumentInvalid);
        }

        var lines = request.Lines;
        if (lines is null || lines.Count == 0 || lines.Count > AccountingSourceDocumentLimitsV1.MaximumLines
            || lines.Any(line => line is null || line.LineNumber < 1)
            || lines.Select(line => line.LineNumber).Distinct().Count() != lines.Count
            || lines.Any(line => !AccountingPostingAccountMappingPayloadV1.IsSelector(line.ItemType)
                || !AccountingPostingAccountMappingPayloadV1.IsSelector(line.LinePurpose)
                || !AccountingPostingAccountMappingPayloadV1.IsSelector(line.VatRateCode)))
        {
            return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.LineInvalid);
        }

        if (request.DocumentTotals is null || request.FunctionalTotals is null
            || !TryTotals(request.DocumentTotals, out var document)
            || !TryTotals(request.FunctionalTotals, out var functional))
        {
            return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.AmountInvalid);
        }

        var parsed = new List<(AccountingSourceDocumentLineV1 Line, decimal Net, decimal Vat, decimal Withholding)>();
        foreach (var line in lines)
        {
            if (!AccountingSourceDocumentAmountsV1.TryParse(line.Net, out var net) || !AccountingSourceDocumentAmountsV1.TryParse(line.Vat, out var vat)
                || !AccountingSourceDocumentAmountsV1.TryParse(line.Withholding, out var withholding))
            {
                return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.AmountInvalid);
            }

            if (withholding > vat)
            {
                return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.WithholdingExceedsVat);
            }

            parsed.Add((line, net, vat, withholding));
        }

        if (request.ExchangeRate is null)
        {
            if (document != functional)
            {
                return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.ExchangeRateInvalid);
            }
        }
        else if (!AccountingSourceDocumentAmountsV1.TryParse(request.ExchangeRate, out var rate) || rate <= 0m)
        {
            return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.ExchangeRateInvalid);
        }

        if (!Consistent(document) || !Consistent(functional)
            || parsed.Sum(item => item.Net) != functional.Net || parsed.Sum(item => item.Vat) != functional.Vat
            || parsed.Sum(item => item.Withholding) != functional.Withholding)
        {
            return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.TotalsMismatch);
        }

        var planned = new List<AccountingSourceDocumentPlannedLineV1>();
        if (functional.Payable > 0m)
        {
            planned.Add(new(AccountingPostingAccountMappingKeysV1.Receivable, AccountingSourceDocumentSidesV1.Debit,
                functional.Payable, null, null, null, null, null));
        }

        foreach (var item in parsed.Where(item => item.Net > 0m).OrderBy(item => item.Line.LineNumber))
        {
            planned.Add(new(AccountingPostingAccountMappingKeysV1.Revenue, AccountingSourceDocumentSidesV1.Credit, item.Net,
                item.Line.LineNumber, item.Line.ItemType, item.Line.LinePurpose, item.Line.IsExport, null));
        }

        foreach (var group in parsed.GroupBy(item => item.Line.VatRateCode, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var amount = group.Sum(item => item.Vat - item.Withholding);
            if (amount > 0m)
            {
                planned.Add(new(AccountingPostingAccountMappingKeysV1.VatOutput, AccountingSourceDocumentSidesV1.Credit,
                    amount, null, null, null, null, group.Key));
            }
        }

        var debit = planned.Where(line => line.Side == AccountingSourceDocumentSidesV1.Debit).Sum(line => line.Amount);
        var credit = planned.Where(line => line.Side == AccountingSourceDocumentSidesV1.Credit).Sum(line => line.Amount);
        if (planned.Count < 2)
        {
            return AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.AmountInvalid);
        }

        return debit != credit
            ? AccountingSourceDocumentPlanV1.Refuse(AccountingSourceDocumentAdmissionReasonsV1.VoucherUnbalanced)
            : new AccountingSourceDocumentPlanV1(null, planned);
    }

    private static bool Consistent((decimal Net, decimal Vat, decimal Withholding, decimal Payable) totals) =>
        totals.Withholding <= totals.Vat && totals.Net + totals.Vat - totals.Withholding == totals.Payable;

    private static bool TryTotals(AccountingSourceDocumentTotalsV1 totals,
        out (decimal Net, decimal Vat, decimal Withholding, decimal Payable) value)
    {
        value = default;
        if (!AccountingSourceDocumentAmountsV1.TryParse(totals.Net, out var net) || !AccountingSourceDocumentAmountsV1.TryParse(totals.Vat, out var vat)
            || !AccountingSourceDocumentAmountsV1.TryParse(totals.Withholding, out var withholding) || !AccountingSourceDocumentAmountsV1.TryParse(totals.Payable, out var payable))
        {
            return false;
        }

        value = (net, vat, withholding, payable);
        return true;
    }
}

/// <summary>Voucher line sides as stored (1 = debit, 2 = credit).</summary>
public static class AccountingSourceDocumentSidesV1
{
    public const int Debit = 1;
    public const int Credit = 2;
}
