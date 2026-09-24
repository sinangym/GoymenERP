using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class ManualBankPaymentTransitionPolicyTests
{
    private static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_requires_another_actor_to_approve()
    {
        var payment = Draft();

        var issues = ManualBankPaymentTransitionPolicy.ValidateApproval(payment, "1", "preparer");

        Assert.Contains(issues, issue => issue.Code == "finance_manual_payment_preparer_cannot_approve");
        Assert.Equal(ManualBankPaymentStage.Draft, payment.Stage);
        Assert.Null(payment.ApprovedBy);
        Assert.Null(payment.MoneyEventId);
    }

    [Fact]
    public void Unknown_bank_result_refuses_record_without_requiring_optional_evidence()
    {
        var approved = ManualBankPaymentTransitionPolicy.Approve(Draft(), "approver", At);

        var issues = ManualBankPaymentTransitionPolicy.ValidateRecord(
            approved, "2", "recorder", ManualBankResultConfirmation.Unknown);

        Assert.Single(issues, issue => issue.Code == "finance_manual_payment_result_not_confirmed");
        Assert.Null(approved.Preparation.BankReference);
        Assert.Null(approved.Preparation.DocumentId);
        Assert.Equal(ManualBankPaymentStage.Approved, approved.Stage);
        Assert.Null(approved.MoneyEventId);
    }

    [Fact]
    public void Occurred_result_records_without_bank_reference_or_document()
    {
        var approved = ManualBankPaymentTransitionPolicy.Approve(Draft(), "approver", At);
        var eventId = Guid.NewGuid();

        var recorded = ManualBankPaymentTransitionPolicy.Record(approved, "recorder", At.AddMinutes(1), eventId);

        Assert.Equal(ManualBankPaymentStage.Recorded, recorded.Stage);
        Assert.Equal("3", recorded.Version);
        Assert.Equal("preparer", recorded.PreparedBy);
        Assert.Equal("approver", recorded.ApprovedBy);
        Assert.Equal("recorder", recorded.RecordedBy);
        Assert.Equal(eventId, recorded.MoneyEventId);
        Assert.Null(recorded.Preparation.BankReference);
        Assert.Null(recorded.Preparation.DocumentId);
    }

    [Fact]
    public void Stale_version_refuses_both_approval_and_record()
    {
        var draft = Draft();
        Assert.Contains(ManualBankPaymentTransitionPolicy.ValidateApproval(draft, "2", "approver"),
            issue => issue.Code == "version_conflict");

        var approved = ManualBankPaymentTransitionPolicy.Approve(draft, "approver", At);
        Assert.Contains(ManualBankPaymentTransitionPolicy.ValidateRecord(
                approved, "1", "recorder", ManualBankResultConfirmation.Occurred),
            issue => issue.Code == "version_conflict");
    }

    private static ManualBankPaymentView Draft() => new(
        Guid.NewGuid(), CompanyId, "1", ManualBankPaymentStage.Draft,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new("125.50", Guid.NewGuid()),
            new(Guid.NewGuid().ToString("D"), "1"), new DateOnly(2026, 9, 24), null,
            BankReference: null, DocumentId: null, Description: "Tedarikci odemesi"),
        "preparer", At.AddMinutes(-1), ApprovedBy: null, ApprovedAtUtc: null,
        RecordedBy: null, RecordedAtUtc: null, MoneyEventId: null);
}

