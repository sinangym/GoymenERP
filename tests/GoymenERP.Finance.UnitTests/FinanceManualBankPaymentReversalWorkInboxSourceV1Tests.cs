using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's pending reversals of recorded manual bank payments
/// (API-23), on SYNTHETIC rows. Only Pending proposals are listed; the proposer is the maker; the rule carries the
/// reversed payment's stored amount and the link opens the payment's own screen.
/// </summary>
public sealed class FinanceManualBankPaymentReversalWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.manual-bank-payment-reversal.v1";

    private protected override string ExpectedPermission => FinancePermissionNames.PaymentReverseApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceManualBankPaymentReversalWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker) =>
        Add(db, company, at, maker, ReversalProposalStatuses.Pending, "100").Proposal.Id;

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        Add(db, company, at, Maker, ReversalProposalStatuses.Approved, "100");
        Add(db, company, at, Maker, ReversalProposalStatuses.Rejected, "100");
    }

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        // A pending proposal whose payment Finance does not find.
        db.PaymentReversalProposals.Add(Proposal(company, Guid.NewGuid(), at, Maker, ReversalProposalStatuses.Pending));

    [Fact]
    public async Task The_rule_carries_the_payments_stored_amount_and_the_link_opens_the_payments_own_screen()
    {
        var seeded = Build(Company, Start, Maker, ReversalProposalStatuses.Pending, "980.4000", "SENTETIK-ODM-1");

        var result = await ListAsync(db =>
        {
            db.ManualBankPayments.Add(seeded.Payment);
            db.PaymentReversalProposals.Add(seeded.Proposal);
        });

        var item = Assert.Single(result.Items!);
        Assert.Equal(seeded.Proposal.Id, item.ItemId);
        Assert.Equal("SENTETIK-ODM-1", item.DocumentLabel);
        Assert.Equal("/finans/odemeler/" + seeded.Payment.Id.ToString("D"), item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceManualBankPaymentReversalWorkInboxSourceV1.ApprovalRequiredRule, null, "980.4", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "manual-bank-payment"
            && reference.RecordId == seeded.Payment.Id && reference.Link == item.Link);
        Assert.Contains(item.References, reference => reference.Role == "party" && reference.RecordId == seeded.Payment.PartyId);
        Assert.Contains(item.References, reference => reference.Role == "bank-account" && reference.RecordId == seeded.Payment.TreasuryAccountId);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
    }

    private static (PaymentReversalProposalRow Proposal, ManualBankPaymentRow Payment) Add(FinanceDbContext db, Guid company,
        DateTimeOffset at, string proposedBy, int status, string amount)
    {
        var seeded = Build(company, at, proposedBy, status, amount, null);
        db.ManualBankPayments.Add(seeded.Payment);
        db.PaymentReversalProposals.Add(seeded.Proposal);
        return seeded;
    }

    private static (PaymentReversalProposalRow Proposal, ManualBankPaymentRow Payment) Build(Guid company,
        DateTimeOffset at, string proposedBy, int status, string amount, string? documentNumber)
    {
        var payment = new ManualBankPaymentRow
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Version = 3,
            PartyId = Guid.NewGuid(),
            CompanyAccountId = Guid.NewGuid(),
            TreasuryAccountId = Guid.NewGuid(),
            CurrencyId = Try,
            AmountText = amount,
            ProfileId = "SENTETIK-profile",
            ProfileVersion = "1",
            OccurredOn = new DateOnly(2026, 11, 19),
            Status = ManualBankPaymentStatuses.Recorded,
            PreparedBy = "SENTETIK-preparer",
            PreparedAtUtc = at.AddDays(-1),
            ApprovedBy = "SENTETIK-payment-approver",
            ApprovedAtUtc = at.AddDays(-1),
            DocumentNumber = documentNumber,
            DocumentNumberFormatVersion = documentNumber is null ? null : "1",
        };
        return (Proposal(company, payment.Id, at, proposedBy, status), payment);
    }

    private static PaymentReversalProposalRow Proposal(Guid company, Guid paymentId, DateTimeOffset at, string proposedBy,
        int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            PaymentId = paymentId,
            OccurredOn = new DateOnly(2026, 11, 20),
            Reason = "SENTETIK-reason",
            Status = status,
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == ReversalProposalStatuses.Pending ? null : Caller,
            DecidedAtUtc = status == ReversalProposalStatuses.Pending ? null : at,
            Version = 1,
        };
}
