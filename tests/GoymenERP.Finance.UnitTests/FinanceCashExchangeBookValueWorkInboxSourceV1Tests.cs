using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over the prepared book values of Finance's currency-exchange transfers
/// (FIN-S14), on SYNTHETIC rows. Only preparations without a decision of their own whose transfer has no approved value
/// are listed; the preparer is the maker; the rules carry the stored TL values, and a legacy preparation without a target
/// value carries its own rule.
/// </summary>
public sealed class FinanceCashExchangeBookValueWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    private protected override string ExpectedKind => "finance.cash-exchange-book-value.v1";

    private protected override string ExpectedPermission => CashExchangeBookValuePermissionsV1.Approve;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceCashExchangeBookValueWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Proposal(company, Guid.NewGuid(), at, maker);
        db.CashExchangeBookValueProposals.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        // A rejected preparation, and a preparation whose transfer already has an approved value.
        var rejected = Proposal(company, Guid.NewGuid(), at, Maker);
        var approvedTransfer = Guid.NewGuid();
        var approved = Proposal(company, approvedTransfer, at, Maker);
        var other = Proposal(company, approvedTransfer, at, Maker);
        db.CashExchangeBookValueProposals.AddRange(rejected, approved, other);
        db.CashExchangeBookValueDecisions.AddRange(
            Decision(company, rejected, approved: false, at),
            Decision(company, approved, approved: true, at));
    }

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Proposal(company, Guid.NewGuid(), at, Maker);
        row.PreparationJson = "{\"SENTETIK\":true}";
        db.CashExchangeBookValueProposals.Add(row);
    }

    [Fact]
    public async Task The_rules_carry_the_stored_tl_values_and_a_legacy_preparation_without_a_target_value_has_its_own_rule()
    {
        var transfer = Guid.NewGuid();
        var current = Proposal(Company, transfer, Start, Maker, "1500.2500", "1499.00");
        var legacy = Proposal(Company, Guid.NewGuid(), Start.AddMinutes(1), Maker, "20", null);
        var cashTransfer = new CashTransferRow
        {
            Id = transfer,
            CompanyId = Company,
            Version = 1,
            Kind = CashTransferKinds.CurrencyExchange,
            SourceAccountId = Guid.NewGuid(),
            SourceCurrencyId = Guid.NewGuid(),
            SourceAmountText = "50",
            SourceProfileId = "SENTETIK-profile",
            SourceProfileVersion = "1",
            TargetAccountId = Guid.NewGuid(),
            TargetCurrencyId = Guid.NewGuid(),
            TargetAmountText = "1500",
            TargetProfileId = "SENTETIK-profile",
            TargetProfileVersion = "1",
            OccurredOn = new DateOnly(2026, 11, 19),
            TransferConfirmed = true,
            RecordedBy = "SENTETIK-recorder",
            RecordedAtUtc = Start.AddDays(-1),
            DocumentNumber = "SENTETIK-VRM-1",
            DocumentNumberFormatVersion = "1",
        };

        var result = await ListAsync(db =>
        {
            db.CashExchangeBookValueProposals.AddRange(current, legacy);
            db.CashTransfers.Add(cashTransfer);
        });

        var items = result.Items!.ToDictionary(item => item.ItemId);
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(FinanceCashExchangeBookValueWorkInboxSourceV1.SourceBookValueRule, null, "1500.25", null, null),
                new WorkApprovalRuleResultV1(FinanceCashExchangeBookValueWorkInboxSourceV1.TargetFunctionalValueRule, null, "1499", null, null),
            ],
            items[current.Id].Rules);
        Assert.Equal("SENTETIK-VRM-1", items[current.Id].DocumentLabel);
        Assert.Equal("/finans/doviz-defter-degeri-onaylari", items[current.Id].Link);
        Assert.Contains(items[current.Id].References, reference => reference.Role == "cash-transfer" && reference.RecordId == transfer);
        Assert.Contains(items[current.Id].References, reference => reference.Role == "evidence-document");
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(FinanceCashExchangeBookValueWorkInboxSourceV1.SourceBookValueRule, null, "20", null, null),
                new WorkApprovalRuleResultV1(FinanceCashExchangeBookValueWorkInboxSourceV1.TargetFunctionalValueMissingRule, null, null, null, null),
            ],
            items[legacy.Id].Rules);
        Assert.Equal(legacy.Id.ToString("D")[..8], items[legacy.Id].DocumentLabel);
    }

    private static CashExchangeBookValueProposalRow Proposal(Guid company, Guid transferId, DateTimeOffset at, string preparedBy,
        string source = "100", string? target = "99") =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            TransferId = transferId,
            TransferSourceJson = "{}",
            TransferSourceHash = new string('c', 64),
            PreparationJson = JsonSerializer.Serialize(new ProposeCashExchangeBookValueV1(transferId, "1", source,
                new CashExchangeBookValueEvidenceV1(Guid.NewGuid(), 1, new string('d', 64)), "SENTETIK-reason", target), Json),
            PreparedBy = preparedBy,
            PreparedAtUtc = at,
        };

    private static CashExchangeBookValueDecisionRow Decision(Guid company, CashExchangeBookValueProposalRow proposal,
        bool approved, DateTimeOffset at) =>
        new()
        {
            CompanyId = company,
            ProposalId = proposal.Id,
            TransferId = proposal.TransferId,
            Approved = approved,
            PreparedBy = proposal.PreparedBy,
            DecidedBy = Caller,
            DecidedAtUtc = at,
            Reason = approved ? null : "SENTETIK-reason",
        };
}
