using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public static class BankReconciliationPolicy
{
    public static BankReconciliationEvaluationV1 Evaluate(
        IReadOnlyCollection<BankStatementReconciliationItemV1>? statementLines,
        IReadOnlyCollection<FinanceEventReconciliationItemV1>? financeEvents)
    {
        if (statementLines is null || statementLines.Count == 0)
        {
            throw Invalid("statementLines");
        }

        if (financeEvents is null || financeEvents.Count == 0)
        {
            throw Invalid("financeEvents");
        }

        var statementIds = new HashSet<Guid>();
        Guid? currencyId = null;
        decimal bankInflow = 0m;
        decimal bankOutflow = 0m;
        foreach (var item in statementLines)
        {
            if (item is null || item.StatementLineId == Guid.Empty)
            {
                throw Invalid("statementLines");
            }

            if (!statementIds.Add(item.StatementLineId))
            {
                throw new BankReconciliationPolicyException(
                    BankReconciliationPolicyCodes.DuplicateReference,
                    "statementLines");
            }

            currencyId = RequireCurrency(currencyId, item.Money, "statementLines");
            var amount = ParseAmount(item.Money.Amount, "statementLines");
            Add(item.Direction, amount, ref bankInflow, ref bankOutflow, "statementLines");
        }

        var eventIds = new HashSet<Guid>();
        decimal financeInflow = 0m;
        decimal financeOutflow = 0m;
        foreach (var item in financeEvents)
        {
            if (item is null || item.MoneyEventId == Guid.Empty)
            {
                throw Invalid("financeEvents");
            }

            if (!eventIds.Add(item.MoneyEventId))
            {
                throw new BankReconciliationPolicyException(
                    BankReconciliationPolicyCodes.DuplicateReference,
                    "financeEvents");
            }

            currencyId = RequireCurrency(currencyId, item.Money, "financeEvents");
            var amount = ParseAmount(item.Money.Amount, "financeEvents");
            Add(item.Direction, amount, ref financeInflow, ref financeOutflow, "financeEvents");
        }

        var currency = currencyId
            ?? throw Invalid("currencyId");
        var totals = new BankReconciliationTotalsV1(
            Money(bankInflow, currency),
            Money(bankOutflow, currency),
            Money(financeInflow, currency),
            Money(financeOutflow, currency));

        return new BankReconciliationEvaluationV1(
            Topology(statementLines.Count, financeEvents.Count),
            totals,
            IsBalanced: bankInflow == financeInflow && bankOutflow == financeOutflow);
    }

    public static BankReconciliationEvaluationV1 RequireBalanced(
        BankReconciliationEvaluationV1? evaluation)
    {
        if (evaluation is null)
        {
            throw Invalid("evaluation");
        }

        if (!evaluation.IsBalanced)
        {
            throw new BankReconciliationPolicyException(
                BankReconciliationPolicyCodes.Unbalanced,
                "totals");
        }

        return evaluation;
    }

    private static Guid RequireCurrency(Guid? expected, MoneyText? money, string field)
    {
        if (money is null || money.CurrencyId == Guid.Empty)
        {
            throw Invalid(field);
        }

        if (expected is { } currencyId && currencyId != money.CurrencyId)
        {
            throw new BankReconciliationPolicyException(
                BankReconciliationPolicyCodes.CurrencyMismatch,
                field);
        }

        return money.CurrencyId;
    }

    private static decimal ParseAmount(string? value, string field)
    {
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
            || amount <= 0m)
        {
            throw Invalid(field);
        }

        return amount;
    }

    private static void Add(
        BankReconciliationDirectionV1 direction,
        decimal amount,
        ref decimal inflow,
        ref decimal outflow,
        string field)
    {
        try
        {
            checked
            {
                switch (direction)
                {
                    case BankReconciliationDirectionV1.Inflow:
                        inflow += amount;
                        break;
                    case BankReconciliationDirectionV1.Outflow:
                        outflow += amount;
                        break;
                    default:
                        throw Invalid(field);
                }
            }
        }
        catch (OverflowException)
        {
            throw Invalid(field);
        }
    }

    private static BankReconciliationTopologyV1 Topology(int bankCount, int financeCount) =>
        (bankCount, financeCount) switch
        {
            (1, 1) => BankReconciliationTopologyV1.OneToOne,
            (1, > 1) => BankReconciliationTopologyV1.OneToMany,
            (> 1, 1) => BankReconciliationTopologyV1.ManyToOne,
            _ => BankReconciliationTopologyV1.ManyToMany
        };

    private static MoneyText Money(decimal amount, Guid currencyId) =>
        new(Text(amount), currencyId);

    private static string Text(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static BankReconciliationPolicyException Invalid(string field) =>
        new(BankReconciliationPolicyCodes.InvalidCandidate, field);
}

public static class BankReconciliationPolicyCodes
{
    public const string InvalidCandidate = "finance_bank_reconciliation_invalid";
    public const string DuplicateReference = "finance_bank_reconciliation_duplicate_reference";
    public const string CurrencyMismatch = "finance_bank_reconciliation_currency_mismatch";
    public const string Unbalanced = "finance_bank_reconciliation_unbalanced";
}

public sealed class BankReconciliationPolicyException(string code, string field)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;

    public string Field { get; } = field;
}
