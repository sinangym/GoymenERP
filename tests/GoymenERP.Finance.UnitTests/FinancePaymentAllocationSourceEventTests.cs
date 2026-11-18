using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>SENTETIK / NOT_ENV owner-service tests; hostile in-memory rows are not SQL constraint acceptance.</summary>
public sealed class FinancePaymentAllocationSourceEventTests
{
    [Theory]
    [InlineData("missing", "finance_allocation_reference_invalid")]
    [InlineData("other-payment", "finance_allocation_reference_invalid")]
    [InlineData("incoming-only", "finance_allocation_reference_invalid")]
    [InlineData("foreign-company", "finance_allocation_reference_invalid")]
    [InlineData("reversed", "finance_source_already_reversed")]
    [InlineData("receivable", "finance_allocation_direction_mismatch")]
    [InlineData("valid", null)]
    public async Task Allocation_uses_only_its_companys_original_outgoing_fact_and_replays_without_another_effect(
        string scenario, string? expectedCode)
    {
        var companyId = Guid.NewGuid(); var otherCompany = Guid.NewGuid();
        var company = new CompanyContext(); company.Set(companyId);
        var database = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(database).Options;
        await using var db = new FinanceDbContext(options, company);
        var user = new User(companyId); var audit = new Collector();
        var payment = new ManualBankPaymentRow
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(),
            TreasuryAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), AmountText = "100.00",
            ProfileId = Guid.NewGuid().ToString("D"), ProfileVersion = "1", Version = 3,
            Status = ManualBankPaymentStatuses.Recorded, OccurredOn = new(2026, 11, 17), RecordedBy = user.UserId,
            RecordedAtUtc = DateTimeOffset.UnixEpoch, BankResultConfirmed = true,
        };
        var item = new FinanceOpenItemRow
        {
            Id = Guid.NewGuid(), CompanyId = companyId, PartyId = payment.PartyId, CompanyAccountId = payment.CompanyAccountId,
            CurrencyId = payment.CurrencyId, OriginalAmountText = "100.00", OutstandingAmountText = "100.00", Version = 1,
            Direction = scenario == "receivable" ? FinanceOpenItemDirectionCodes.Receivable : FinanceOpenItemDirectionCodes.Payable,
            SourceKind = scenario == "receivable" ? FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
                : FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment,
            SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceVersion = "1", SourceBatchId = Guid.NewGuid(),
            SourceBatchVersion = "1", SourceBatchChecksum = new string('a', 64), Status = FinanceOpenItemStatuses.Open,
        };
        db.ManualBankPayments.Add(payment); db.OpenItems.Add(item);
        if (scenario is "other-payment" or "reversed" or "receivable" or "valid")
            db.OutgoingMoneyEvents.Add(Outgoing(companyId, scenario == "other-payment" ? Guid.NewGuid() : payment.Id));
        if (scenario == "incoming-only")
            db.MoneyEvents.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, SourceId = payment.Id,
                Kind = MoneyEventKinds.ManualBankReceiptRecorded, PartyId = payment.PartyId,
                CompanyAccountId = payment.CompanyAccountId, TreasuryAccountId = payment.TreasuryAccountId,
                CurrencyId = payment.CurrencyId, AmountText = payment.AmountText, ProfileId = payment.ProfileId,
                ProfileVersion = "1", OccurredOn = payment.OccurredOn, RecordedBy = user.UserId,
                RecordedAtUtc = DateTimeOffset.UnixEpoch, CorrelationId = "SENTETIK-incoming" });
        if (scenario == "reversed")
            db.OutgoingMoneyEventReversals.Add(new() { Id = Guid.NewGuid(), CompanyId = companyId, PaymentId = payment.Id,
                ProposalId = Guid.NewGuid(), OriginalEventId = db.OutgoingMoneyEvents.Local.Single().Id,
                PartyId = payment.PartyId, CompanyAccountId = payment.CompanyAccountId, TreasuryAccountId = payment.TreasuryAccountId,
                CurrencyId = payment.CurrencyId, AmountText = payment.AmountText, ProfileId = payment.ProfileId, ProfileVersion = "1",
                OccurredOn = payment.OccurredOn, RecordedBy = user.UserId, RecordedAtUtc = DateTimeOffset.UnixEpoch,
                CorrelationId = "SENTETIK-reversal" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (scenario == "foreign-company")
        {
            // Adversarial data proves the active tenant filter; no claim that SQL permits the broken foreign key.
            var foreign = new CompanyContext(); foreign.Set(otherCompany);
            await using var foreignDb = new FinanceDbContext(options, foreign);
            foreignDb.OutgoingMoneyEvents.Add(Outgoing(otherCompany, payment.Id));
            await foreignDb.SaveChangesAsync();
        }
        var outgoingBefore = await db.OutgoingMoneyEvents.CountAsync();
        var incomingBefore = await db.MoneyEvents.CountAsync();
        var reversalsBefore = await db.OutgoingMoneyEventReversals.CountAsync();
        var service = new FinancePaymentAllocationService(db, company, user, new AllowAll(), audit,
            new("synthetic.finance.allocations.write", ["SENTETIK"], "Web"), TimeProvider.System);
        var context = new FinanceContext(companyId, user.UserId, "SENTETIK-original-source", null);
        var request = new CreateFinancePaymentAllocationRequest(Guid.NewGuid(), payment.Id, "3", item.Id, "1", "10.00", "SENTETIK source proof");
        var key = new CommandMetadata("SENTETIK-original-source");
        var result = await service.CreateAsync(context, key, request, default);
        Assert.Equal(expectedCode is null ? CommandOutcome.Succeeded : CommandOutcome.ValidationFailed, result.Outcome);
        if (expectedCode is null) Assert.Empty(result.Issues);
        else { Assert.Equal(expectedCode, Assert.Single(result.Issues).Code); Assert.Null(result.Resource); }
        Assert.False(result.IsReplay);
        await AssertEconomicStateAsync();
        Assert.Single(await db.Commands.ToListAsync());
        db.ChangeTracker.Clear();
        var replay = await service.CreateAsync(context with { CorrelationId = "SENTETIK-replay" }, key, request, default);
        Assert.True(replay.IsReplay); Assert.Equal(result.Outcome, replay.Outcome);
        Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
        if (expectedCode is not null) Assert.Equal(expectedCode, Assert.Single(replay.Issues).Code);
        Assert.Single(await db.Commands.ToListAsync());
        await AssertEconomicStateAsync();

        OutgoingMoneyEventRow Outgoing(Guid owner, Guid source) => new()
        {
            Id = Guid.NewGuid(), CompanyId = owner, PaymentId = source, PartyId = payment.PartyId,
            CompanyAccountId = payment.CompanyAccountId, TreasuryAccountId = payment.TreasuryAccountId,
            CurrencyId = payment.CurrencyId, AmountText = payment.AmountText, ProfileId = payment.ProfileId, ProfileVersion = "1",
            OccurredOn = payment.OccurredOn, RecordedBy = user.UserId, RecordedAtUtc = DateTimeOffset.UnixEpoch,
            CorrelationId = "SENTETIK-outgoing",
        };
        async Task AssertEconomicStateAsync()
        {
            var original = await db.ManualBankPayments.AsNoTracking().SingleAsync();
            Assert.Equal(("100.00", 3L, ManualBankPaymentStatuses.Recorded), (original.AmountText, original.Version, original.Status));
            var open = await db.OpenItems.AsNoTracking().SingleAsync();
            Assert.Equal(expectedCode is null ? ("90.00", 2L) : ("100.00", 1L), (open.OutstandingAmountText, open.Version));
            Assert.Equal(outgoingBefore, await db.OutgoingMoneyEvents.CountAsync());
            Assert.Equal(incomingBefore, await db.MoneyEvents.CountAsync());
            Assert.Equal(reversalsBefore, await db.OutgoingMoneyEventReversals.CountAsync());
            Assert.Equal(expectedCode is null ? 1 : 0, await db.PaymentAllocations.CountAsync());
            Assert.Equal(expectedCode is null ? 1 : 0, await db.PaymentAllocationStates.CountAsync());
            Assert.Equal(expectedCode is null ? 1 : 0, audit.Events.Count);
        }
    }
    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-source-owner";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }
    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id,
            CancellationToken token) => Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class Collector : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft item) => Events.Add(item);
    }
}
