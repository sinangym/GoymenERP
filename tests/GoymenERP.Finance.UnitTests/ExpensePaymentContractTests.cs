using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Finance.UnitTests;

/// <summary>FNS-D25 contract shape and validator rules. SENTETIK / NOT_ENV.</summary>
public sealed class ExpensePaymentContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Item = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Payment = Guid.Parse("66666666-6666-4666-8666-666666666666");

    private static ExpensePaymentPreparation Preparation(
        string description = "SENTETIK kirtasiye gideri",
        string? reference = null,
        Guid? item = null,
        DateOnly? valueOn = null) => new(
        ExpensePaymentSourceKind.Cash,
        Account,
        item ?? Item,
        new MoneyText("125.50", Currency),
        new ProfileReference("77777777-7777-4777-8777-777777777777", "9007199254740993"),
        new DateOnly(2026, 11, 10),
        valueOn,
        reference,
        description);

    [Fact]
    public void A_view_round_trips_with_the_item_identity_and_the_description()
    {
        var value = new ExpensePaymentView(
            Payment,
            Company,
            ExpensePaymentStatus.Approved,
            Preparation(),
            "SENTETIK-maker",
            new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero),
            "SENTETIK-checker",
            new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero),
            null,
            null,
            false,
            "2");

        var json = JsonSerializer.Serialize(value, FinanceJson.CreateOptions());
        var restored = JsonSerializer.Deserialize<ExpensePaymentView>(json, FinanceJson.CreateOptions());

        Assert.Equal(value, restored);
        Assert.Equal(Item, restored!.Preparation.ExpenseItemId);
        Assert.Contains("\"sourceKind\":\"Cash\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Approved\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_preparation_passes_the_shape_rules()
    {
        Assert.Empty(ExpensePaymentValidator.ValidateShape(Preparation()));
        Assert.Empty(ExpensePaymentValidator.ValidateShape(
            Preparation(reference: "SENTETIK-REF", valueOn: new DateOnly(2026, 11, 10))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void The_description_is_mandatory(string description)
    {
        var issues = ExpensePaymentValidator.ValidateShape(Preparation(description));

        Assert.Contains(issues, issue => issue.Code == "expense_payment_description_required");
    }

    [Fact]
    public void A_description_longer_than_the_limit_is_refused()
    {
        var issues = ExpensePaymentValidator.ValidateShape(
            Preparation(new string('x', ExpensePaymentValidator.DescriptionMaximumLength + 1)));
        Assert.Contains(issues, issue => issue.Code == "expense_payment_description_too_long");

        Assert.Empty(ExpensePaymentValidator.ValidateShape(
            Preparation(new string('x', ExpensePaymentValidator.DescriptionMaximumLength))));
    }

    [Fact]
    public void The_expense_item_is_kept_by_identity_and_an_empty_identity_is_refused()
    {
        var issues = ExpensePaymentValidator.ValidateShape(Preparation(item: Guid.Empty));

        Assert.Contains(issues, issue => issue.Code == "expense_item_required");
    }

    [Fact]
    public void A_blank_or_overlong_reference_and_an_early_value_date_are_refused()
    {
        Assert.Contains(ExpensePaymentValidator.ValidateShape(Preparation(reference: " ")),
            issue => issue.Code == "expense_payment_reference_invalid");
        Assert.Contains(ExpensePaymentValidator.ValidateShape(
                Preparation(reference: new string('r', ExpensePaymentValidator.ReferenceMaximumLength + 1))),
            issue => issue.Code == "expense_payment_reference_invalid");
        Assert.Contains(ExpensePaymentValidator.ValidateShape(Preparation(valueOn: new DateOnly(2026, 11, 9))),
            issue => issue.Code == "value_date_before_occurred_date");
    }

    [Fact]
    public void An_undefined_source_kind_is_refused()
    {
        var issues = ExpensePaymentValidator.ValidateShape(
            Preparation() with { SourceKind = (ExpensePaymentSourceKind)7 });

        Assert.Contains(issues, issue => issue.Code == "expense_payment_source_invalid");
    }

    [Fact]
    public void The_preparer_cannot_approve_but_another_actor_can()
    {
        var request = new ApproveExpensePaymentRequest(Payment, "1");

        Assert.False(ExpensePaymentValidator.CanApprove(
            ExpensePaymentStatus.Draft, "SENTETIK-maker", "SENTETIK-maker", request));
        Assert.True(ExpensePaymentValidator.CanApprove(
            ExpensePaymentStatus.Draft, "SENTETIK-maker", "SENTETIK-checker", request));
        Assert.False(ExpensePaymentValidator.CanApprove(
            ExpensePaymentStatus.Approved, "SENTETIK-maker", "SENTETIK-checker", request));
        Assert.False(ExpensePaymentValidator.CanApprove(
            ExpensePaymentStatus.Draft, "SENTETIK-maker", "SENTETIK-checker",
            new ApproveExpensePaymentRequest(Payment, "01")));
    }

    [Fact]
    public void The_record_needs_the_approved_state_and_an_explicit_confirmation()
    {
        Assert.Empty(ExpensePaymentValidator.ValidateRecord(
            ExpensePaymentStatus.Approved, new RecordExpensePaymentRequest(Payment, "2", PaymentConfirmed: true)));

        Assert.Contains(ExpensePaymentValidator.ValidateRecord(
                ExpensePaymentStatus.Approved, new RecordExpensePaymentRequest(Payment, "2", PaymentConfirmed: false)),
            issue => issue.Code == "expense_payment_not_confirmed");
        Assert.Contains(ExpensePaymentValidator.ValidateRecord(
                ExpensePaymentStatus.Draft, new RecordExpensePaymentRequest(Payment, "2", PaymentConfirmed: true)),
            issue => issue.Code == "expense_payment_not_approved");
        Assert.Contains(ExpensePaymentValidator.ValidateRecord(
                ExpensePaymentStatus.Recorded, new RecordExpensePaymentRequest(Payment, "3", PaymentConfirmed: true)),
            issue => issue.Code == "expense_payment_not_approved");
        Assert.Contains(ExpensePaymentValidator.ValidateRecord(
                ExpensePaymentStatus.Approved, new RecordExpensePaymentRequest(Guid.Empty, "2", PaymentConfirmed: true)),
            issue => issue.Code == "identity_required");

        foreach (var version in new[] { "", "0", "01", "+1", " 1", "9223372036854775808" })
        {
            Assert.Contains(ExpensePaymentValidator.ValidateRecord(
                    ExpensePaymentStatus.Approved,
                    new RecordExpensePaymentRequest(Payment, version, PaymentConfirmed: true)),
                issue => issue.Code == "expected_version_invalid");
        }
    }

    [Fact]
    public void Draft_create_and_replace_identifiers_are_distinct()
    {
        Assert.True(ExpensePaymentValidator.IsValidDraftIdentity(new(null, null, Preparation())));
        Assert.False(ExpensePaymentValidator.IsValidDraftIdentity(new(null, "1", Preparation())));
        Assert.False(ExpensePaymentValidator.IsValidDraftIdentity(new(Payment, null, Preparation())));
        Assert.False(ExpensePaymentValidator.IsValidDraftIdentity(new(Guid.Empty, "1", Preparation())));
        Assert.True(ExpensePaymentValidator.IsValidDraftIdentity(new(Payment, "9007199254740993", Preparation())));
    }

    [Fact]
    public void The_treasury_balance_refusal_codes_are_stable()
    {
        Assert.Equal("finance_treasury_balance_unavailable", TreasuryBalanceAnswer.UnavailableCode);
        Assert.Equal("finance_treasury_balance_insufficient", TreasuryBalanceAnswer.InsufficientCode);
    }
}
