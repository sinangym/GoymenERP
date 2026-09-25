using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public static class CashCountPolicy
{
    public static CashCountDraftV1 Prepare(
        Guid companyId,
        Guid countedBy,
        PrepareCashCountV1? request,
        DateTimeOffset countedAtUtc)
    {
        if (request is null)
        {
            throw Invalid("request");
        }

        RequireId(companyId, "companyId");
        RequireId(countedBy, "countedBy");
        RequireId(request.CountId, "countId");
        RequireId(request.TreasuryAccountId, "treasuryAccountId");
        RequireUtc(countedAtUtc, "countedAtUtc");

        var book = ParseBalance(request.BookBalance, "bookBalance");
        var counted = ParseBalance(request.CountedBalance, "countedBalance");
        if (request.BookBalance.CurrencyId != request.CountedBalance.CurrencyId)
        {
            throw new CashCountPolicyException(
                CashCountPolicyCodes.CurrencyMismatch,
                "countedBalance");
        }

        var signedDifference = counted - book;
        var direction = signedDifference switch
        {
            > 0m => CashCountDifferenceDirectionV1.Over,
            < 0m => CashCountDifferenceDirectionV1.Short,
            _ => CashCountDifferenceDirectionV1.Balanced
        };
        var difference = decimal.Abs(signedDifference);
        var reason = NormalizeReason(request.DifferenceReason);
        if (difference != 0m && reason is null)
        {
            throw new CashCountPolicyException(
                CashCountPolicyCodes.DifferenceReasonRequired,
                "differenceReason");
        }

        var currencyId = request.BookBalance.CurrencyId;
        return new CashCountDraftV1(
            request.CountId,
            companyId,
            request.TreasuryAccountId,
            currencyId,
            new MoneyText(Text(book), currencyId),
            new MoneyText(Text(counted), currencyId),
            new MoneyText(Text(difference), currencyId),
            direction,
            reason,
            countedBy,
            countedAtUtc,
            Version: 1,
            RequiresApproval: difference != 0m,
            CreatesMoneyEvent: false);
    }

    private static decimal ParseBalance(MoneyText? money, string field)
    {
        if (money is null || money.CurrencyId == Guid.Empty)
        {
            throw Invalid(field);
        }

        var value = money.Amount;
        if (string.IsNullOrEmpty(value)
            || value[0] == '.'
            || value[^1] == '.'
            || value.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || value.Count(character => character == '.') > 1
            || !decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount)
            || amount < 0m)
        {
            throw Invalid(field);
        }

        return amount;
    }

    private static string? NormalizeReason(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void RequireId(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw Invalid(field);
        }
    }

    private static void RequireUtc(DateTimeOffset value, string field)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw Invalid(field);
        }
    }

    private static string Text(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static CashCountPolicyException Invalid(string field) =>
        new(CashCountPolicyCodes.InvalidRequest, field);
}

public static class CashCountPolicyCodes
{
    public const string InvalidRequest = "finance_cash_count_invalid";
    public const string CurrencyMismatch = "finance_cash_count_currency_mismatch";
    public const string DifferenceReasonRequired = "finance_cash_count_difference_reason_required";
}

public sealed class CashCountPolicyException(string code, string field)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;

    public string Field { get; } = field;
}
