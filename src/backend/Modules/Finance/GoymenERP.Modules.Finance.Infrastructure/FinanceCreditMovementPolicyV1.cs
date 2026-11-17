using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>Source shape only. It calculates neither interest nor tax, bank net, exchange rate or installment settlement.</summary>
public static class FinanceCreditMovementPolicyV1
{
    private static readonly HashSet<string> Kinds = ["principal", "interest", "bsmv", "kkdf", "commission", "fee"];
    public static IReadOnlyList<ValidationIssue> Validate(CreditMovementProposeV1 value)
    {
        var issues = new List<ValidationIssue>();
        void Issue(string field, string code) => issues.Add(new(field, code));
        if (value.CreditAccountId == Guid.Empty || !Text(value.ExpectedCreditVersion, 64)) Issue("creditAccountId", "credit_movement_identity_required");
        if (!Enum.IsDefined(value.Kind)) Issue("kind", "credit_movement_kind_invalid");
        if (value.SourceDocument is not { } source || !Text(source.Reference, 200) || !Text(source.Revision, 100) || source.DocumentDate == default)
            Issue("sourceDocument", "credit_movement_source_required");
        if (value.TransactionDate == default || value.AccountingDate == default) Issue("accountingDate", "credit_movement_date_required");
        if (value.ReplacesOpeningMovementId is null && value.AccountingDate != value.TransactionDate)
            Issue("accountingDate", "credit_movement_date_must_equal_transaction_date");
        if (!Text(value.Reason, 1000)) Issue("reason", "credit_movement_reason_required");
        if (value.Components is null || value.Components.Count is < 1 or > 6 || value.Components.Any(x => x is null))
            Issue("components", "credit_movement_components_required");
        else
        {
            if (value.Components.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count() != value.Components.Count)
                Issue("components", "credit_movement_duplicate_component");
            foreach (var component in value.Components)
            {
                if (!Kinds.Contains(component.Kind)) Issue("components", "credit_movement_component_invalid");
                if (!Amount(component.Amount, true)) Issue("components", "credit_movement_amount_invalid");
                if (component.FunctionalAmount is { } functional && !Amount(functional, true)) Issue("components", "credit_movement_functional_amount_invalid");
            }
            if (!value.Components.Any(x => Amount(x.Amount, false))) Issue("components", "credit_movement_positive_amount_required");
            if (value.Kind is CreditMovementKindV1.Drawdown or CreditMovementKindV1.OpeningBalance
                && !value.Components.Any(x => x.Kind == "principal" && Amount(x.Amount, false)))
                Issue("components", "credit_movement_principal_required");
            if (value.Kind == CreditMovementKindV1.InterestAccrual && value.Components.Any(x => x.Kind == "principal"))
                Issue("components", "credit_accrual_cannot_change_principal");
        }
        if (value.BankEffect is { } bank && (bank.BankAccountId == Guid.Empty || !Amount(bank.Amount, false) || !Text(bank.SourceReference, 200)
            || value.Kind is not (CreditMovementKindV1.Drawdown or CreditMovementKindV1.Repayment)))
            Issue("bankEffect", "credit_movement_bank_effect_invalid");
        if (value.ReplacesOpeningMovementId is { } replaces && (replaces == Guid.Empty || value.Kind != CreditMovementKindV1.OpeningBalance
            || !Text(value.ExpectedOpeningMovementVersion, 64))) Issue("replacesOpeningMovementId", "credit_opening_correction_invalid");
        if (value.ReplacesOpeningMovementId is null && value.ExpectedOpeningMovementVersion is not null)
            Issue("expectedOpeningMovementVersion", "credit_opening_correction_invalid");
        return issues;
    }
    internal static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();
    internal static bool Amount(string? value, bool zeroAllowed) => value is { Length: <= 128 }
        && (zeroAllowed ? FinanceExactDecimalText.TryParseNonNegative(value, out _) : FinanceExactDecimalText.TryParsePositive(value, out _));
}
