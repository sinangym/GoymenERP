using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-SC47 (FNS-D93, FNS-D94): the one deterministic check of an imported bank repayment plan. The amounts are taken as
/// given and only their totals are checked; interest is never calculated. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceCreditPlanCalculatorTests
{
    private static readonly DateOnly First = new(2026, 12, 15);

    [Fact]
    public void A_plan_whose_lines_and_principal_total_hold_is_valid_with_exact_unrounded_totals()
    {
        var result = FinanceCreditPlanCalculator.Validate("1000.00",
        [
            Line(1, "541.2345", "500.00", "33.0000", "1.6500", "6.5845"),
            Line(2, "530.917", "500.00", "25.000", "1.250", "4.667"),
        ]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal(new CreditPlanTotalsView("1072.1515", "1000.00", "58.0000", "2.9000", "11.2515"), result.Totals);
    }

    [Fact]
    public void Different_scales_of_the_same_amount_hold_because_the_comparison_is_exact()
    {
        var result = FinanceCreditPlanCalculator.Validate("1000",
            [Line(1, "1000.000", "1000.0", "0", "0.00", "0")]);

        Assert.True(result.IsValid);
        Assert.Equal("1000.0", result.Totals!.Principal);
    }

    [Fact]
    public void An_installment_that_differs_from_its_components_by_the_smallest_digit_is_refused()
    {
        var result = FinanceCreditPlanCalculator.Validate("500.00",
            [Line(1, "541.2346", "500.00", "33.0000", "1.6500", "6.5845")]);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(("installments[0].installment", FinanceCreditPlanCalculator.InstallmentSumMismatch),
            (issue.Field, issue.Code));
        // The lines were readable, so the totals are still shown to the user.
        Assert.NotNull(result.Totals);
    }

    [Fact]
    public void A_principal_total_other_than_the_credit_amount_is_refused_on_the_plan()
    {
        var result = FinanceCreditPlanCalculator.Validate("1000.00",
        [
            Line(1, "510.00", "500.00", "10.00", "0", "0"),
            Line(2, "509.99", "499.99", "10.00", "0", "0"),
        ]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("installments", FinanceCreditPlanCalculator.PrincipalTotalMismatch), (issue.Field, issue.Code));
        Assert.Equal("999.99", result.Totals!.Principal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("1,00")]
    [InlineData("1e3")]
    [InlineData(".5")]
    [InlineData("5.")]
    public void A_credit_amount_that_is_not_exact_positive_text_is_refused(string? amount)
    {
        var result = FinanceCreditPlanCalculator.Validate(amount, [Line(1, "1", "1", "0", "0", "0")]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("principalAmount", FinanceCreditPlanCalculator.PrincipalAmountInvalid), (issue.Field, issue.Code));
    }

    [Fact]
    public void A_credit_amount_longer_than_its_column_is_refused()
    {
        var result = FinanceCreditPlanCalculator.Validate(
            new string('1', FinanceCreditPlanCalculator.AmountMaximumLength + 1), [Line(1, "1", "1", "0", "0", "0")]);

        Assert.Contains(result.Issues, issue => issue.Field == "principalAmount"
            && issue.Code == FinanceCreditPlanCalculator.PrincipalAmountInvalid);
    }

    [Fact]
    public void An_empty_or_missing_plan_is_refused()
    {
        foreach (var plan in new IReadOnlyList<CreditPlanInstallmentInput>?[] { null, [] })
        {
            var result = FinanceCreditPlanCalculator.Validate("1", plan);
            var issue = Assert.Single(result.Issues);
            Assert.Equal(("installments", FinanceCreditPlanCalculator.PlanEmpty), (issue.Field, issue.Code));
            Assert.Null(result.Totals);
        }
    }

    [Fact]
    public void A_plan_longer_than_the_technical_cap_is_refused_without_reading_its_lines()
    {
        var lines = Enumerable.Range(1, FinanceCreditPlanCalculator.MaximumInstallments + 1)
            .Select(sequence => Line(sequence, "1", "1", "0", "0", "0")).ToList();

        var result = FinanceCreditPlanCalculator.Validate("601", lines);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("installments", FinanceCreditPlanCalculator.PlanTooLong), (issue.Field, issue.Code));
    }

    [Fact]
    public void A_plan_at_the_technical_cap_is_read()
    {
        var lines = Enumerable.Range(1, FinanceCreditPlanCalculator.MaximumInstallments)
            .Select(sequence => Line(sequence, "1", "1", "0", "0", "0")).ToList();

        Assert.True(FinanceCreditPlanCalculator.Validate("600", lines).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Sequences_run_one_to_n_in_the_given_order(int secondSequence)
    {
        var result = FinanceCreditPlanCalculator.Validate("2",
            [Line(1, "1", "1", "0", "0", "0"), Line(secondSequence, "1", "1", "0", "0", "0")]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("installments[1].sequence", FinanceCreditPlanCalculator.SequenceInvalid), (issue.Field, issue.Code));
    }

    [Theory]
    [InlineData("installment")]
    [InlineData("principal")]
    [InlineData("interest")]
    [InlineData("bsmv")]
    [InlineData("kkdf")]
    public void An_unreadable_amount_is_refused_on_its_field_and_hides_the_totals(string field)
    {
        var line = Line(1, "1", "1", "0", "0", "0");
        line = field switch
        {
            "installment" => line with { Installment = "-1" },
            "principal" => line with { Principal = "1.0.0" },
            "interest" => line with { Interest = "" },
            "bsmv" => line with { Bsmv = "0,5" },
            _ => line with { Kkdf = null! },
        };

        var result = FinanceCreditPlanCalculator.Validate("1", [line]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("installments[0]." + field, FinanceCreditPlanCalculator.AmountInvalid), (issue.Field, issue.Code));
        Assert.Null(result.Totals);
    }

    [Fact]
    public void A_null_line_is_refused_on_its_index()
    {
        var result = FinanceCreditPlanCalculator.Validate("1", [Line(1, "1", "1", "0", "0", "0"), null!]);

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("installments[1]", FinanceCreditPlanCalculator.AmountInvalid), (issue.Field, issue.Code));
        Assert.Null(result.Totals);
    }

    [Fact]
    public void Every_issue_of_a_plan_is_reported_together()
    {
        var result = FinanceCreditPlanCalculator.Validate("0",
        [
            Line(2, "10", "5", "1", "1", "1"),
            Line(2, "10", "5", "5", "0", "0"),
        ]);

        Assert.Equal(
        [
            ("principalAmount", FinanceCreditPlanCalculator.PrincipalAmountInvalid),
            ("installments[0].sequence", FinanceCreditPlanCalculator.SequenceInvalid),
            ("installments[0].installment", FinanceCreditPlanCalculator.InstallmentSumMismatch),
        ], result.Issues.Select(issue => (issue.Field, issue.Code)));
    }

    private static CreditPlanInstallmentInput Line(int sequence, string installment, string principal, string interest,
        string bsmv, string kkdf) =>
        new(sequence, First.AddMonths(sequence - 1), installment, principal, interest, bsmv, kkdf);
}
