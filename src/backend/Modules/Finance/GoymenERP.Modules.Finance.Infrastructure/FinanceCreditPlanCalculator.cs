using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-SC47 (FNS-D93, FNS-D94): the one deterministic check of an imported bank repayment plan. The proposal, the approval
/// and the side-effect-free validation endpoint all call it; the screen never re-implements it.
/// <list type="bullet">
/// <item>The plan's amounts are taken AS GIVEN: exact non-negative decimal text (digits and at most one inner dot), never
/// rounded, never converted. The system never calculates interest.</item>
/// <item>Each line (FNS-D99): installment = principal + interest + BSMV + KKDF within 0,01 TL per line. The tolerance is
/// Sinan's decided TRY amount, not a currency's smallest unit: for any other currency no tolerance is decided, so the plan
/// is refused fail-closed (<see cref="ToleranceUndecided"/>, NEEDS-SINAN). Without a currency no tolerance is granted.</item>
/// <item>Charges (commission/fee lines on the bank plan): FNS-D94 leaves their treatment open and FNS-D99's equality has no
/// charge term, so a plan carrying any charge is refused fail-closed (<see cref="ChargesUndecided"/>, NEEDS-SINAN); a
/// charge is never added to the installment's components.</item>
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
    internal const string ToleranceUndecided = "credit_plan_tolerance_undecided";
    internal const string ChargesUndecided = "credit_plan_charges_undecided";

    /// <summary>FNS-D99 (SINAN_KARARLARI_2026-11-16_DALGA4_EK23.md): "satır başına 0,01 TL" — one hundredth of a lira.</summary>
    private static readonly FinanceExactDecimalText TurkishLiraLineTolerance = new(1, 2);

    /// <summary>The plan's verdict: its issues and, when every line could be read, its exact totals.</summary>
    internal sealed record Result(IReadOnlyList<ValidationIssue> Issues, CreditPlanTotalsView? Totals)
    {
        public bool IsValid => Issues.Count == 0;
    }

    internal static Result Validate(string? principalAmount, IReadOnlyList<CreditPlanInstallmentInput>? installments,
        Guid? currencyId = null)
    {
        var issues = new List<ValidationIssue>();
        var tolerance = new FinanceExactDecimalText(0, 0);
        if (currencyId == SettingsCurrencyIdentitiesV1.TurkishLira) tolerance = TurkishLiraLineTolerance;
        else if (currencyId is not null) issues.Add(new ValidationIssue("currencyId", ToleranceUndecided));
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
        var totalIncludedCharges = zero;
        var totalSeparateCharges = zero;
        var hasCharges = false;
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
            if (line.Charges is { } charges)
            {
                if (charges.Count > 100)
                {
                    issues.Add(new ValidationIssue(prefix + "charges", "credit_plan_charges_too_many"));
                    everyLineReadable = false;
                    continue;
                }
                for (var chargeIndex = 0; chargeIndex < charges.Count; chargeIndex++)
                {
                    var charge = charges[chargeIndex];
                    var chargeField = prefix + "charges[" + chargeIndex.ToString(CultureInfo.InvariantCulture) + "]";
                    if (charge is null || !Enum.IsDefined(charge.Treatment)
                        || !Canonical(charge.Description, 200) || !Canonical(charge.SourceDocumentReference, 100))
                    {
                        issues.Add(new ValidationIssue(chargeField, "credit_plan_charge_source_invalid"));
                        everyLineReadable = false;
                        continue;
                    }
                    if (!TryRead(charge.Amount, chargeField + ".amount", issues, out var chargeAmount))
                    {
                        everyLineReadable = false;
                        continue;
                    }
                    hasCharges = true;
                    if (charge.Treatment == CreditChargeTreatmentV1.IncludedInInstallment)
                        totalIncludedCharges = totalIncludedCharges.Add(chargeAmount);
                    else totalSeparateCharges = totalSeparateCharges.Add(chargeAmount);
                }
                if (charges.Count != 0) issues.Add(new ValidationIssue(prefix + "charges", ChargesUndecided));
            }
            var difference = installment.CompareTo(components) >= 0
                ? installment.Subtract(components) : components.Subtract(installment);
            if (difference.CompareTo(tolerance) > 0)
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
            totalInterest.Text, totalBsmv.Text, totalKkdf.Text,
            hasCharges ? new(totalIncludedCharges.Text, totalSeparateCharges.Text) : null));
    }

    private static bool Canonical(string? text, int maximum) => !string.IsNullOrWhiteSpace(text)
        && text == text.Trim() && text.Length <= maximum;

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
