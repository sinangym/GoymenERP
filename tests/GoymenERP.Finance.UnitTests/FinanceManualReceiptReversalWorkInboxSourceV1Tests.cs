using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's pending reversals of recorded manual receipts
/// (FIN-T06/B1-B2), on SYNTHETIC rows. Only Pending proposals are listed; the proposer is the maker; the rule carries the
/// original receipt's stored amount and the receipt reference links the receipt's own screen.
/// </summary>
public sealed class FinanceManualReceiptReversalWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.manual-receipt-reversal.v1";

    private protected override string ExpectedPermission => FinancePermissionNames.ReverseApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceManualReceiptReversalWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker) =>
        Add(db, company, at, maker, ReversalProposalStatuses.Pending, "100").Proposal.Id;

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        Add(db, company, at, Maker, ReversalProposalStatuses.Approved, "100");
        Add(db, company, at, Maker, ReversalProposalStatuses.Rejected, "100");
    }

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        // A pending proposal whose original receipt Finance does not find.
        db.ReversalProposals.Add(Proposal(company, Guid.NewGuid(), at, Maker, ReversalProposalStatuses.Pending));

    [Fact]
    public async Task The_rule_carries_the_receipts_stored_amount_and_the_receipt_reference_links_its_own_screen()
    {
        var seeded = Build(Company, Start, Maker, ReversalProposalStatuses.Pending, "75.10", "SENTETIK-THS-1");

        var result = await ListAsync(db =>
        {
            db.ReceiptDrafts.Add(seeded.Receipt);
            db.ReversalProposals.Add(seeded.Proposal);
        });

        var item = Assert.Single(result.Items!);
        Assert.Equal("SENTETIK-THS-1", item.DocumentLabel);
        Assert.Equal("/finans/ters-kayit-onaylari", item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceManualReceiptReversalWorkInboxSourceV1.ApprovalRequiredRule, null, "75.1", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "manual-receipt" && reference.RecordId == seeded.Receipt.Id
            && reference.Link == "/finans/tahsilatlar/" + seeded.Receipt.Id.ToString("D"));
        Assert.Contains(item.References, reference => reference.Role == "party" && reference.RecordId == seeded.Receipt.PartyId);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
    }

    private static (ReversalProposalRow Proposal, ReceiptDraftRow Receipt) Add(FinanceDbContext db, Guid company,
        DateTimeOffset at, string proposedBy, int status, string amount)
    {
        var seeded = Build(company, at, proposedBy, status, amount, null);
        db.ReceiptDrafts.Add(seeded.Receipt);
        db.ReversalProposals.Add(seeded.Proposal);
        return seeded;
    }

    private static (ReversalProposalRow Proposal, ReceiptDraftRow Receipt) Build(Guid company,
        DateTimeOffset at, string proposedBy, int status, string amount, string? documentNumber)
    {
        var receipt = new ReceiptDraftRow
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Version = 2,
            PartyId = Guid.NewGuid(),
            CompanyAccountId = Guid.NewGuid(),
            TreasuryAccountId = Guid.NewGuid(),
            CurrencyId = Try,
            AmountText = amount,
            ProfileId = "SENTETIK-profile",
            ProfileVersion = "1",
            OccurredOn = new DateOnly(2026, 11, 19),
            CreatedAtUtc = at.AddDays(-1),
            CreatedBy = "SENTETIK-preparer",
            UpdatedAtUtc = at.AddDays(-1),
            UpdatedBy = "SENTETIK-preparer",
            Stage = ReceiptStages.Recorded,
            RecordedAtUtc = at.AddDays(-1),
            RecordedBy = "SENTETIK-preparer",
            DocumentNumber = documentNumber,
            DocumentNumberFormatVersion = documentNumber is null ? null : "1",
        };
        return (Proposal(company, receipt.Id, at, proposedBy, status), receipt);
    }

    private static ReversalProposalRow Proposal(Guid company, Guid sourceId, DateTimeOffset at, string proposedBy, int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            OriginalSourceId = sourceId,
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
