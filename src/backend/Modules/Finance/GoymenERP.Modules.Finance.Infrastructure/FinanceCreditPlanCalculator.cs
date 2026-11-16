using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-SC47 (FNS-D93, FNS-D94): the one deterministic check of an imported bank repayment plan. The proposal, the approval
/// and the side-effect-free validation endpoint all call it; the screen never re-implements it.
/// <list type="bullet">
/// <item>The plan's amounts are taken AS GIVEN: exact non-negative decimal text (digits and at most one inner dot), never
/// rounded, never converted. The system never calculates interest.</item>
/// <item>Each line: installment = principal + interest + BSMV + KKDF, exactly.</item>
/// <item>The plan: the sum of the principals = the credit amount (exact positive text), exactly.</item>
/// <item>Sequences run 1..n in the given order. Dates are carried as given (FNS-D94 checks the totals only).</item>
/// </list>
/// Each failure is one issue on the field it names; a plan with any issue is not valid.
/// </summary>
internal static class FinanceCreditPlanCalculator
{
    /// <summary>The longest amount text a column holds.</summary>
    internal const int AmountMaximumLength = 64;

    /// <summary>
    /// A technical cap on the lines of one plan, so one request cannot carry an unbounded body. Not a business rule: a
    /// monthly plan of several decades fits it.
    /// </summary>
    internal const int MaximumInstallments = 600;

    internal const string PlanEmpty = "credit_plan_empty";
    internal const string PlanTooLong = "credit_plan_too_long";
    internal const string SequenceInvalid = "credit_plan_sequence_invalid";
    internal const string AmountInvalid = "credit_plan_amount_invalid";
    internal const string InstallmentSumMismatch = "credit_plan_installment_sum_mismatch";
    internal const string PrincipalTotalMismatch = "credit_plan_principal_total_mismatch";
    internal const string PrincipalAmountInvalid = "credit_amount_invalid";

    /// <summary>The plan's verdict: its issues and, when every line could be read, its exact totals.</summary>
    internal sealed record Result(IReadOnlyList<ValidationIssue> Issues, CreditPlanTotalsView? Totals)
    {
        public bool IsValid => Issues.Count == 0;
    }

    internal static Result Validate(string? principalAmount, IReadOnlyList<CreditPlanInstallmentInput>? installments)
    {
        var issues = new List<ValidationIssue>();
        var amount = default(FinanceExactDecimalText);
        var amountReadable = principalAmount is not null && principalAmount.Length <= AmountMaximumLength
            && FinanceExactDecimalText.TryParsePositive(principalAmount, out amount);
        if (!amountReadable)
        {
            issues.Add(new ValidationIssue("principalAmount", PrincipalAmountInvalid));
        }

        if (installments is null || installments.Count == 0)
        {
            issues.Add(new ValidationIssue("installments", PlanEmpty));
            return new Result(issues, null);
        }

        if (installments.Count > MaximumInstallments)
        {
            issues.Add(new ValidationIssue("installments", PlanTooLong));
            return new Result(issues, null);
        }

        var zero = new FinanceExactDecimalText(0, 0);
        var totalInstallment = zero;
        var totalPrincipal = zero;
        var totalInterest = zero;
        var totalBsmv = zero;
        var totalKkdf = zero;
        var everyLineReadable = true;
        for (var index = 0; index < installments.Count; index++)
        {
            var line = installments[index];
            var prefix = "installments[" + index.ToString(CultureInfo.InvariantCulture) + "].";
            if (line is null)
            {
                issues.Add(new ValidationIssue("installments[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                    AmountInvalid));
                everyLineReadable = false;
                continue;
            }

            if (line.Sequence != index + 1)
            {
                issues.Add(new ValidationIssue(prefix + "sequence", SequenceInvalid));
            }

            var readable = TryRead(line.Installment, prefix + "installment", issues, out var installment);
            readable &= TryRead(line.Principal, prefix + "principal", issues, out var principal);
            readable &= TryRead(line.Interest, prefix + "interest", issues, out var interest);
            readable &= TryRead(line.Bsmv, prefix + "bsmv", issues, out var bsmv);
            readable &= TryRead(line.Kkdf, prefix + "kkdf", issues, out var kkdf);
            if (!readable)
            {
                everyLineReadable = false;
                continue;
            }

            var components = principal.Add(interest).Add(bsmv).Add(kkdf);
            if (installment.CompareTo(components) != 0)
            {
                issues.Add(new ValidationIssue(prefix + "installment", InstallmentSumMismatch));
            }

            totalInstallment = totalInstallment.Add(installment);
            totalPrincipal = totalPrincipal.Add(principal);
            totalInterest = totalInterest.Add(interest);
            totalBsmv = totalBsmv.Add(bsmv);
            totalKkdf = totalKkdf.Add(kkdf);
        }

        if (!everyLineReadable)
        {
            return new Result(issues, null);
        }

        if (amountReadable && totalPrincipal.CompareTo(amount) != 0)
        {
            issues.Add(new ValidationIssue("installments", PrincipalTotalMismatch));
        }

        return new Result(issues, new CreditPlanTotalsView(totalInstallment.Text, totalPrincipal.Text,
            totalInterest.Text, totalBsmv.Text, totalKkdf.Text));
    }

    private static bool TryRead(string? value, string field, List<ValidationIssue> issues,
        out FinanceExactDecimalText parsed)
    {
        if (value is not null && value.Length <= AmountMaximumLength
            && FinanceExactDecimalText.TryParseNonNegative(value, out parsed))
        {
            return true;
        }

        parsed = default;
        issues.Add(new ValidationIssue(field, AmountInvalid));
        return false;
    }
}
