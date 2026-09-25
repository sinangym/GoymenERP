using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class CompanyCardExpensePolicyTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000015");

    [Fact]
    public void Complete_expense_slip_is_accepted_without_becoming_a_card_payment()
    {
        var expense = Valid();

        var issues = CompanyCardExpensePolicy.Validate(expense, CompanyId);

        Assert.Empty(issues);
        Assert.Equal(CompanyCardFinancialEventKind.Expense, expense.EventKind);
        Assert.NotEqual(Guid.Empty, expense.DocumentId);
        Assert.NotEmpty(expense.SpentBy);
        Assert.NotEmpty(expense.ExpenseType);
        Assert.NotEmpty(expense.ContextId);
    }

    [Theory]
    [InlineData(CompanyCardFinancialEventKind.LimitAdjustment)]
    [InlineData(CompanyCardFinancialEventKind.Statement)]
    [InlineData(CompanyCardFinancialEventKind.Payment)]
    public void Limit_statement_and_payment_events_cannot_be_recorded_as_expense_slips(
        CompanyCardFinancialEventKind kind)
    {
        var issues = CompanyCardExpensePolicy.Validate(Valid() with { EventKind = kind }, CompanyId);

        Assert.Contains(issues, issue => issue.Code == "finance_company_card_slip_is_expense");
    }

    [Fact]
    public void Foreign_company_and_empty_source_identities_are_refused()
    {
        var expense = Valid() with
        {
            Id = Guid.Empty,
            CardId = Guid.Empty,
            DocumentId = Guid.Empty,
            Money = new("1250.50", Guid.Empty),
        };

        var issues = CompanyCardExpensePolicy.Validate(expense, Guid.NewGuid());

        Assert.Contains(issues, issue => issue.Code == "company_context_invalid");
        Assert.Contains(issues, issue => issue.Code == "identity_required");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("1,25")]
    public void Non_positive_or_non_lossless_amount_is_refused(string amount)
    {
        var issues = CompanyCardExpensePolicy.Validate(
            Valid() with { Money = new(amount, Guid.NewGuid()) },
            CompanyId);

        Assert.Contains(issues, issue => issue.Code == "finance_company_card_amount_invalid");
    }

    [Fact]
    public void Missing_document_spender_expense_type_or_context_is_refused()
    {
        var expense = Valid() with
        {
            DocumentId = Guid.Empty,
            SpentBy = "",
            ExpenseType = " ",
            ContextType = "",
            ContextId = "",
        };

        var issues = CompanyCardExpensePolicy.Validate(expense, CompanyId);

        Assert.Contains(issues, issue => issue.Code == "identity_required");
        Assert.Contains(issues, issue => issue.Code == "finance_company_card_expense_evidence_required");
    }

    [Fact]
    public void Noncanonical_versions_profile_and_missing_spend_date_are_refused()
    {
        var expense = Valid() with
        {
            Version = "0",
            CardVersion = "card-v4",
            Profile = new ProfileReference("not-a-profile-id", "profile-v2"),
            SpentOn = default,
        };

        var issues = CompanyCardExpensePolicy.Validate(expense, CompanyId);

        Assert.Contains(issues, issue => issue.Code == "finance_company_card_version_required");
        Assert.Contains(issues, issue => issue.Code == "finance_company_card_spent_on_required");
    }

    private static CompanyCardExpenseV1 Valid() => new(
        Guid.NewGuid(),
        CompanyId,
        "1",
        Guid.NewGuid(),
        "4",
        CompanyCardFinancialEventKind.Expense,
        new MoneyText("1250.50", Guid.NewGuid()),
        new ProfileReference(Guid.NewGuid().ToString("D"), "2"),
        new DateOnly(2026, 9, 24),
        "SLIP-2026-0047",
        Guid.NewGuid(),
        "finance-user",
        "travel",
        "purchase-order",
        "PO-2026-0047");
}
