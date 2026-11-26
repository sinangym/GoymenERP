using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Only the approved package supplies accounts and sides. Every amount is an actual document component.</summary>
public static class CreditMovementJournalComponentsV1
{
    public const string BankKey = "credit.bank";
    public const string TotalKey = "credit.total";
    public static string ComponentKey(string kind) => "credit.component." + kind;
    public static string? Purpose(string kind) => kind switch {
        "Drawdown" => "finance.credit.drawdown", "Repayment" => "finance.credit.repayment",
        "InterestAccrual" => "finance.credit.interest-accrual", _ => null };

    public static IReadOnlyList<NormalJournalExactSourceLineV1>? TryCreate(CreditMovementSourceV1 source,
        NormalJournalPostingRuleV1 rule, int scale)
    {
        if (source.ReversesMovementId is not null || Purpose(source.Kind) is not { } purpose || rule.PostingPurpose != purpose
            || scale is < 0 or > Money.MaximumScale || source.Components is not { Count: > 0 and <= 7 }) return null;
        var amounts = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var component in source.Components)
        {
            if (component.Kind is not ("principal" or "interest" or "bsmv" or "kkdf" or "commission" or "fee" or "vat")
                || !Read(component.Amount, scale, out var amount) || amounts.ContainsKey(ComponentKey(component.Kind))) return null;
            amounts.Add(ComponentKey(component.Kind), amount);
        }
        var positive = amounts.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        if (positive.Count == 0) return null;
        if (source.BankEffect is { } bank)
        {
            if (!Read(bank.Amount, scale, out var amount) || amount <= 0 || source.Kind is not ("Drawdown" or "Repayment")) return null;
            positive.Add(BankKey, amount);
        }
        // An approved bankless accrual mapping may use the exact documented component total on its counterpart side.
        // It is never the bank amount of a drawdown/repayment, nor a calculated interest or tax.
        else if (rule.Mappings.Any(x => x.MappingKey == TotalKey))
        {
            var total = positive.Values.Aggregate(AccountingInventoryCostAmountsV1.FromDecimal(0m),
                (sum, amount) => AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(amount)));
            if (!AccountingInventoryCostAmountsV1.TryExactDecimal(total, scale, out var amount)) return null;
            positive.Add(TotalKey, amount);
        }
        var lines = new List<NormalJournalExactSourceLineV1>();
        foreach (var mapping in rule.Mappings)
        {
            if (!positive.TryGetValue(mapping.MappingKey, out var amount) || !Enum.IsDefined(mapping.Side)
                || lines.Any(x => x.MappingKey == mapping.MappingKey && x.Side == mapping.Side.ToString())) return null;
            lines.Add(new(mapping.MappingKey, mapping.Side.ToString(), amount.ToString("0.############################", CultureInfo.InvariantCulture)));
        }
        if (positive.Keys.Any(key => !lines.Any(x => x.MappingKey == key)) || !NormalJournalExactSourceAmountsV1.Valid(lines)) return null;
        return lines;
    }

    private static bool Read(string value, int scale, out decimal amount)
    {
        amount = 0m;
        if (value is { Length: > 0 and <= 128 } && value[0] != '.' && value[^1] != '.'
            && value.All(c => c is '0' or '.') && value.Count(c => c == '.') <= 1) return true;
        return NormalJournalDraftSourcePreparationV1.TryExactAmount(value, out amount) && Money.FitsScale(amount, scale);
    }
}
