using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>SENTETIK / NOT_ENV actual-service evidence. SQL transaction/constraint acceptance is separate.</summary>
public sealed class FinanceAllocationServicePriorityTests
{
    [Theory]
    [InlineData("300.00", "500.00", "300.01", null, false, "finance_allocation_exceeds_source")]
    [InlineData("900.00", "500.00", "500.01", null, false, "finance_allocation_exceeds_open_item")]
    [InlineData("1000.00", "1000.00", "100.01", "100.00", true, "finance_allocation_exceeds_transferred_amount")]
    [InlineData("1000.00", "1000.00", "100.01", "900.00", false, "finance_allocation_exceeds_transferred_amount")]
    public async Task Global_balance_codes_and_transferred_capacity_refuse_without_business_changes_and_replay_once(
        string receiptAmount, string itemAmount, string requested, string? transferred, bool selectSuccessor, string code)
    {
        var companyId = Guid.NewGuid();
        var company = new CompanyContext(); company.Set(companyId);
        var party = Guid.NewGuid(); var account = Guid.NewGuid(); var currency = Guid.NewGuid();
        var successor = Guid.NewGuid(); var successorAccount = Guid.NewGuid();
        var actor = new SyntheticUser(companyId);
        var context = new FinanceContext(companyId, actor.UserId, "SENTETIK-first-allocation", null);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var receipt = new ReceiptDraftRow
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PartyId = party, CompanyAccountId = account,
            CurrencyId = currency, AmountText = receiptAmount, Stage = ReceiptStages.Recorded,
            Version = 3, TreasuryAccountId = Guid.NewGuid(), ProfileId = Guid.NewGuid().ToString("D"), ProfileVersion = "1",
            OccurredOn = new(2026, 11, 17), ValueOn = new(2026, 11, 17), CreatedBy = actor.UserId, UpdatedBy = actor.UserId,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            RecordedBy = actor.UserId, RecordedAtUtc = DateTimeOffset.UnixEpoch,
        };
        var item = new FinanceOpenItemRow
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PartyId = party, CompanyAccountId = account,
            CurrencyId = currency, OriginalAmountText = itemAmount, OutstandingAmountText = itemAmount,
            Direction = FinanceOpenItemDirectionCodes.Receivable,
            SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1", SourceBatchId = Guid.NewGuid(),
            SourceBatchVersion = "1", SourceBatchChecksum = new string('a', 64),
            Status = FinanceOpenItemStatuses.Open, Version = 1, CreatedBy = actor.UserId, UpdatedBy = actor.UserId,
            CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };
        db.ReceiptDrafts.Add(receipt); db.OpenItems.Add(item);
        db.MoneyEvents.Add(new()
        {
            Id = Guid.NewGuid(), CompanyId = companyId, SourceId = receipt.Id, Kind = MoneyEventKinds.ManualBankReceiptRecorded, PartyId = party,
            CompanyAccountId = account, TreasuryAccountId = receipt.TreasuryAccountId, CurrencyId = currency,
            AmountText = receiptAmount, ProfileId = receipt.ProfileId, ProfileVersion = "1",
            OccurredOn = receipt.OccurredOn, ValueOn = receipt.ValueOn, RecordedAtUtc = DateTimeOffset.UnixEpoch,
            RecordedBy = actor.UserId, CorrelationId = "SENTETIK-original-money",
        });
        if (transferred is not null)
        {
            foreach (var (kind, source) in new[] { (FinanceBalanceAssignmentKinds.Receipt, receipt.Id),
                         (FinanceBalanceAssignmentKinds.OpenItem, item.Id) })
                db.PartyBalanceAssignments.Add(new()
                {
                    CompanyId = companyId, Kind = kind, SourceId = source, OriginalPartyId = party, OriginalAccountId = account,
                    CurrentPartyId = successor, CurrentAccountId = successorAccount, CurrencyId = currency,
                    AvailableAmountText = transferred, Version = 1, LastTransferId = Guid.NewGuid(),
                });
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var assignmentBefore = await AssignmentStateAsync(db);
        var collector = new Collector();
        var service = new FinanceAllocationService(db, company, actor, new AllowAll(), collector,
            new("synthetic.finance.allocations.write", ["SENTETIK"], "Web"), TimeProvider.System);
        var request = new CreateFinanceAllocationRequest(Guid.NewGuid(), receipt.Id, "3", item.Id, "1", requested,
            "SENTETIK allocation boundary", selectSuccessor ? successor : null, selectSuccessor ? successorAccount : null);
        var metadata = new CommandMetadata("SENTETIK-allocation-priority");

        var refused = await service.CreateAsync(context, metadata, request, default);
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Equal(code, Assert.Single(refused.Issues).Code);
        Assert.Null(refused.Resource); Assert.False(refused.IsReplay);
        await AssertUnchangedBusinessAsync();
        var command = Assert.Single(await db.Commands.AsNoTracking().ToListAsync());
        Assert.Equal(FinanceAllocationService.CreateCommandName, command.CommandName);
        Assert.Equal(actor.UserId, command.ActorId);
        Assert.Equal(receipt.Id, command.DraftId);

        db.ChangeTracker.Clear();
        var replay = await service.CreateAsync(context with { CorrelationId = "SENTETIK-second-allocation" }, metadata, request, default);
        Assert.True(replay.IsReplay); Assert.Equal(refused.Outcome, replay.Outcome);
        Assert.Equal(code, Assert.Single(replay.Issues).Code);
        Assert.Equal(refused.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Single(await db.Commands.AsNoTracking().ToListAsync());
        await AssertUnchangedBusinessAsync();

        async Task AssertUnchangedBusinessAsync()
        {
            var storedReceipt = await db.ReceiptDrafts.AsNoTracking().SingleAsync();
            Assert.Equal((receiptAmount, 3L, ReceiptStages.Recorded),
                (storedReceipt.AmountText, storedReceipt.Version, storedReceipt.Stage));
            var storedItem = await db.OpenItems.AsNoTracking().SingleAsync();
            Assert.Equal((itemAmount, itemAmount, 1L, FinanceOpenItemStatuses.Open),
                (storedItem.OriginalAmountText, storedItem.OutstandingAmountText, storedItem.Version, storedItem.Status));
            Assert.Equal(assignmentBefore, await AssignmentStateAsync(db));
            var money = Assert.Single(await db.MoneyEvents.AsNoTracking().ToListAsync());
            Assert.Equal((receipt.Id, receiptAmount, currency), (money.SourceId, money.AmountText, money.CurrencyId));
            Assert.Empty(await db.OutgoingMoneyEvents.AsNoTracking().ToListAsync());
            Assert.Empty(await db.OutgoingMoneyEventReversals.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Allocations.AsNoTracking().ToListAsync());
            Assert.Empty(await db.SourceAllocationStates.AsNoTracking().ToListAsync());
            Assert.Empty(await db.PartyBalanceTransfers.AsNoTracking().ToListAsync());
            Assert.Empty(collector.Events);
        }
    }

    private static async Task<(int, Guid, Guid, Guid, string, long, Guid)[]> AssignmentStateAsync(FinanceDbContext db) =>
        (await db.PartyBalanceAssignments.AsNoTracking().OrderBy(row => row.Kind).ToListAsync())
            .Select(row => (row.Kind, row.SourceId, row.CurrentPartyId, row.CurrentAccountId,
                row.AvailableAmountText, row.Version, row.LastTransferId)).ToArray();

    private sealed class SyntheticUser(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-allocation-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }

    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class Collector : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft draft) => Events.Add(draft);
    }
}
