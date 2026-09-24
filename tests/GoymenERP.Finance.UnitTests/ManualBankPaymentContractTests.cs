using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Finance.UnitTests;

public sealed class ManualBankPaymentContractTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Party = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Bank = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Payment = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static ManualBankPaymentPreparation Preparation(string? bankReference = null) => new(
        Party,
        Account,
        Bank,
        new MoneyText("1250.50", Currency),
        new ProfileReference("77777777-7777-7777-7777-777777777777", "9007199254740993"),
        new DateOnly(2026, 9, 23),
        new DateOnly(2026, 9, 24),
        bankReference,
        "SENTETIK manual outgoing payment");

    [Fact]
    public void A_now_round_trips_without_bank_reference_or_document()
    {
        var value = new ManualBankPaymentView(
            Payment,
            Company,
            ManualBankPaymentStatus.Approved,
            Preparation(),
            "SENTETIK-maker",
            new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero),
            "SENTETIK-approver",
            new DateTimeOffset(2026, 9, 23, 10, 5, 0, TimeSpan.Zero),
            null,
            null,
            false,
            "2");

        var json = JsonSerializer.Serialize(value, FinanceJson.CreateOptions());
        var restored = JsonSerializer.Deserialize<ManualBankPaymentView>(json, FinanceJson.CreateOptions());

        Assert.Equal(value, restored);
        Assert.Null(restored!.Preparation.BankReference);
        Assert.DoesNotContain("document", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Maker_cannot_approve_but_another_actor_can()
    {
        var request = new ApproveManualBankPaymentRequest(Payment, "1");

        Assert.False(ManualBankPaymentValidator.CanApprove(
            ManualBankPaymentStatus.Draft, "SENTETIK-maker", "SENTETIK-maker", request));
        Assert.True(ManualBankPaymentValidator.CanApprove(
            ManualBankPaymentStatus.Draft, "SENTETIK-maker", "SENTETIK-checker", request));
        Assert.False(ManualBankPaymentValidator.CanApprove(
            ManualBankPaymentStatus.Approved, "SENTETIK-maker", "SENTETIK-checker", request));
    }

    [Fact]
    public void Unknown_bank_result_is_refused_and_confirmed_result_needs_no_reference()
    {
        var unknown = ManualBankPaymentValidator.ValidateRecord(
            ManualBankPaymentStatus.Approved,
            new RecordManualBankPaymentRequest(Payment, "2", BankResultConfirmed: false));
        Assert.Contains(unknown, issue => issue.Code == "bank_result_not_confirmed");

        var confirmed = ManualBankPaymentValidator.ValidateRecord(
            ManualBankPaymentStatus.Approved,
            new RecordManualBankPaymentRequest(Payment, "2", BankResultConfirmed: true));
        Assert.Empty(confirmed);
    }

    [Fact]
    public void Record_requires_the_approved_state_and_a_canonical_positive_version()
    {
        var wrongState = ManualBankPaymentValidator.ValidateRecord(
            ManualBankPaymentStatus.Draft,
            new RecordManualBankPaymentRequest(Payment, "2", BankResultConfirmed: true));
        Assert.Contains(wrongState, issue => issue.Code == "manual_bank_payment_not_approved");

        foreach (var version in new[] { "", "0", "01", "+1", " 1", "9223372036854775808" })
        {
            var issues = ManualBankPaymentValidator.ValidateRecord(
                ManualBankPaymentStatus.Approved,
                new RecordManualBankPaymentRequest(Payment, version, BankResultConfirmed: true));
            Assert.Contains(issues, issue => issue.Code == "expected_version_invalid");
        }
    }

    [Fact]
    public void Draft_create_and_replace_identifiers_are_distinct()
    {
        Assert.True(ManualBankPaymentValidator.IsValidDraftIdentity(new(null, null, Preparation())));
        Assert.False(ManualBankPaymentValidator.IsValidDraftIdentity(new(null, "1", Preparation())));
        Assert.False(ManualBankPaymentValidator.IsValidDraftIdentity(new(Payment, null, Preparation())));
        Assert.True(ManualBankPaymentValidator.IsValidDraftIdentity(new(Payment, "9007199254740993", Preparation())));
    }

    [Fact]
    public void Permission_literals_are_owner_declarations_not_grants()
    {
        Assert.Equal("finance.manual-bank-payment.draft.save", FinancePermissionNames.ManualBankPaymentDraftSave);
        Assert.Equal("finance.manual-bank-payment.draft.read", FinancePermissionNames.ManualBankPaymentDraftRead);
        Assert.Equal("finance.manual-bank-payment.approve", FinancePermissionNames.ManualBankPaymentApprove);
        Assert.Equal("finance.manual-bank-payment.record", FinancePermissionNames.ManualBankPaymentRecord);
    }
}
