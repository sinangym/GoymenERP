using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>SENTETIK / NOT_ENV service evidence; does not replace SQL isolation, atomic audit or migration acceptance.</summary>
public sealed class FinancePartyBalanceTransferServiceTests
{
    [Fact]
    public async Task Full_currency_balances_move_once_without_rewriting_documents_or_creating_money_events()
    {
        await using var run = new Run();
        var original = await run.SeedAsync("100.0100");
        await run.SeedAsync("12.3456", Guid.NewGuid());
        var preview = await run.PreviewAsync();
        Assert.Equal(2, preview.Currencies.Count);
        Assert.Equal(2, preview.OpenItemCount);
        var request = run.Request(preview);
        var result = await run.Service.RecordAsync(run.Context, new("SENTETIK-key"), request, default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        var replay = await run.Service.RecordAsync(run.Context with { CorrelationId = "SENTETIK-retry" }, new("SENTETIK-key"), request, default);
        Assert.True(replay.Replayed);
        Assert.Equal(run.Context.CorrelationId, result.OriginalCorrelationId);
        Assert.Equal(result.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.DoesNotContain("originalCorrelationId", System.Text.Json.JsonSerializer.Serialize(result, FinanceJson.CreateOptions()));
        Assert.Single(await run.Db.PartyBalanceTransfers.ToListAsync());
        Assert.Equal(2, await run.Db.PartyBalanceAssignments.CountAsync());
        Assert.Empty(await run.Db.MoneyEvents.ToListAsync());
        Assert.Empty(await run.Db.OutgoingMoneyEvents.ToListAsync());
        var source = await run.Db.OpenItems.AsNoTracking().SingleAsync(x => x.Id == original);
        Assert.Equal((run.Party, run.Account, "100.0100", "100.0100", 2L),
            (source.PartyId, source.CompanyAccountId, source.OriginalAmountText, source.OutstandingAmountText, source.Version));
        var old = await run.BalanceAsync(run.Party, run.Account);
        Assert.All(old.Currencies, c => Assert.Equal(PartyLedgerBalanceSide.Zero, c.Balance.Side));
        var successor = await run.BalanceAsync(run.Successor, run.SuccessorAccount);
        Assert.Equal("100.0100", successor.Currencies.Single(c => c.CurrencyId == run.Currency).Balance.Amount.Money!.Amount);
        Assert.Single(run.Audit.Events);
        Assert.Equal(FinancePartyBalanceTransferPermissionsV1.Record, run.Audit.Events[0].PermissionCode);
    }

    [Fact]
    public async Task Legacy_receipt_without_correlation_is_replayed_without_inventing_a_retry_correlation()
    {
        await using var run = new Run();
        await run.SeedAsync("10.00");
        var request = run.Request(await run.PreviewAsync());
        var result = await run.Service.RecordAsync(run.Context, new("SENTETIK-legacy"), request, default);
        var stored = await run.Db.PartyBalanceTransferCommands.SingleAsync();
        stored.ResultJson = System.Text.Json.JsonSerializer.Serialize(result, FinanceJson.CreateOptions());
        await run.Db.SaveChangesAsync();
        run.Db.ChangeTracker.Clear();
        var replay = await run.Service.RecordAsync(run.Context with { CorrelationId = "SENTETIK-retry" }, new("SENTETIK-legacy"), request, default);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.Replayed);
        Assert.Null(replay.OriginalCorrelationId);
        Assert.Single(await run.Db.PartyBalanceTransfers.ToListAsync());
        Assert.Single(run.Audit.Events);
    }

    [Fact]
    public async Task Later_original_document_difference_stays_original_until_another_explicit_transfer()
    {
        await using var run = new Run();
        await run.SeedAsync("100.00");
        var first = await run.PreviewAsync();
        Assert.Equal(CommandOutcome.Succeeded, (await run.Service.RecordAsync(run.Context, new("SENTETIK-first"), run.Request(first), default)).Outcome);
        var correction = await run.SeedAsync("20.00");
        Assert.Equal("20.00", Assert.Single((await run.BalanceAsync(run.Party, run.Account)).Currencies).Balance.Amount.Money!.Amount);
        Assert.Equal("100.00", Assert.Single((await run.BalanceAsync(run.Successor, run.SuccessorAccount)).Currencies).Balance.Amount.Money!.Amount);
        Assert.False(await run.Db.PartyBalanceAssignments.AnyAsync(x => x.SourceId == correction));
        var second = await run.PreviewAsync();
        Assert.Equal("20.00", Assert.Single(second.Currencies).Amount);
        Assert.Equal(CommandOutcome.Succeeded, (await run.Service.RecordAsync(run.Context, new("SENTETIK-second"), run.Request(second), default)).Outcome);
        Assert.Equal("120.00", Assert.Single((await run.BalanceAsync(run.Successor, run.SuccessorAccount)).Currencies).Balance.Amount.Money!.Amount);
    }

    [Fact]
    public async Task Stale_snapshot_changed_legal_link_closed_period_and_missing_evidence_leave_no_voucher()
    {
        await using var run = new Run();
        await run.SeedAsync("100.00");
        var preview = await run.PreviewAsync();
        await run.SeedAsync("1.00");
        Assert.Equal(CommandOutcome.VersionConflict, (await run.Service.RecordAsync(run.Context, new("SENTETIK-stale"), run.Request(preview), default)).Outcome);
        preview = await run.PreviewAsync();
        run.Period.Outcome = RecordPeriodOutcome.NotOpen;
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.Service.RecordAsync(run.Context, new("SENTETIK-period"), run.Request(preview), default)).Outcome);
        run.Period.Outcome = RecordPeriodOutcome.Open;
        run.Evidence.Outcome = ReadOutcome.NotFound;
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.Service.RecordAsync(run.Context, new("SENTETIK-evidence"), run.Request(preview), default)).Outcome);
        run.Evidence.Outcome = ReadOutcome.Found;
        run.Owner.ChangeDuringRecord = true;
        Assert.Equal(CommandOutcome.VersionConflict, (await run.Service.RecordAsync(run.Context, new("SENTETIK-owner"), run.Request(preview), default)).Outcome);
        Assert.Empty(await run.Db.PartyBalanceTransfers.ToListAsync());
        Assert.Empty(await run.Db.PartyBalanceTransferCommands.ToListAsync());
        Assert.Empty(run.Audit.Events);
    }

    [Fact]
    public void Bounded_position_keeps_later_increase_original_and_rejects_spending_the_successors_excess()
    {
        var originalParty = Guid.NewGuid(); var originalAccount = Guid.NewGuid(); var currency = Guid.NewGuid();
        var row = new FinancePartyBalanceAssignmentRow { CompanyId = Guid.NewGuid(), SourceId = Guid.NewGuid(), Kind = 1,
            OriginalPartyId = originalParty, OriginalAccountId = originalAccount, CurrentPartyId = Guid.NewGuid(),
            CurrentAccountId = Guid.NewGuid(), CurrencyId = currency, AvailableAmountText = "100.0000", LastTransferId = Guid.NewGuid(), Version = 1 };
        var positions = new FinanceAllocationPositions(originalParty, originalAccount, currency, "125.0001", [row]);
        Assert.Equal("25.0001", positions.Select(null, null).AvailableAmount);
        var successor = positions.Select(row.CurrentPartyId, row.CurrentAccountId);
        Assert.Throws<FinanceAllocationPolicyException>(() => positions.Spend(successor, "100.0001"));
        Assert.Equal("100.0000", row.AvailableAmountText);
        positions.Spend(successor, "60.0000");
        Assert.Equal("40.0000", row.AvailableAmountText);
        Assert.Equal(2, row.Version);
    }

    private sealed class Run : IAsyncDisposable
    {
        public Guid Company { get; } = Guid.NewGuid();
        public Guid Party { get; } = Guid.NewGuid();
        public Guid Account { get; } = Guid.NewGuid();
        public Guid Successor { get; } = Guid.NewGuid();
        public Guid SuccessorAccount { get; } = Guid.NewGuid();
        public Guid Currency { get; } = Guid.NewGuid();
        public FinanceDbContext Db { get; }
        public FinanceContext Context { get; }
        public FinancePartyBalanceTransferServiceV1 Service { get; }
        public PartyLedgerService Ledger { get; }
        public OwnerPort Owner { get; }
        public EvidencePort Evidence { get; } = new();
        public PeriodPort Period { get; } = new();
        public Collector Audit { get; } = new();
        public Run()
        {
            var company = new CompanyContext(); company.Set(Company);
            var user = new User(Company);
            Db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
            Context = new(Company, user.UserId, "SENTETIK-correlation", null);
            var permissions = new AllowAll();
            Ledger = new(Db, company, user, permissions);
            Owner = new(new(Company, Party, Account, "2", "2", "SENTETIK-P", "SENTETIK eski", Successor, SuccessorAccount,
                "1", "1", "SENTETIK-S", "SENTETIK yeni", FinancePartyBalanceTransferLinkKindV1.Succession,
                new(2026, 11, 17), "SENTETIK hukuki dayanak", DateTimeOffset.UnixEpoch));
            Service = new(Db, company, user, permissions, Owner, Evidence, Ledger, Period, Audit,
                new(FinancePartyBalanceTransferPermissionsV1.Record, ["SENTETIK-YETKI"], "Web"), TimeProvider.System);
        }
        public async Task<Guid> SeedAsync(string amount, Guid? currency = null)
        {
            var id = Guid.NewGuid();
            Db.OpenItems.Add(new() { Id = id, CompanyId = Company, Direction = FinanceOpenItemDirectionCodes.Receivable,
                SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
                SourceVersion = "1", SourceBatchId = Guid.NewGuid(), SourceBatchVersion = "1", SourceBatchChecksum = new string('a', 64),
                PartyId = Party, CompanyAccountId = Account, CurrencyId = currency ?? Currency,
                OriginalAmountText = amount, OutstandingAmountText = amount, Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = Context.ActorId, UpdatedBy = Context.ActorId, CreatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedAtUtc = DateTimeOffset.UnixEpoch });
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear(); return id;
        }
        public async Task<FinancePartyBalanceTransferPreviewV1> PreviewAsync() =>
            (await Service.PreviewAuthorizedAsync(Context, new(Party, Account, Successor, SuccessorAccount), default)).Preview!;
        public RecordFinancePartyBalanceTransferV1 Request(FinancePartyBalanceTransferPreviewV1 preview) =>
            new(Guid.NewGuid(), new(Party, Account, Successor, SuccessorAccount), preview.SnapshotVersion,
                new(2026, 11, 17), "SENTETIK hukuki devir", new(Guid.NewGuid(), 1, new string('b', 64)));
        public async Task<PartyBalanceView> BalanceAsync(Guid party, Guid account) =>
            (await Ledger.SummarizeAuthorizedAsync(Context, new(party, account), default)).Balance!;
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => "SENTETIK-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => id == company;
    }
    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token) => Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class OwnerPort(FinancePartyBalanceTransferOwnerProofV1 proof) : IFinancePartyBalanceTransferOwnerPortV1
    {
        public bool ChangeDuringRecord { get; set; }
        private int changingReads;
        public Task<FinancePartyBalanceTransferOwnerAnswerV1> ReadAsync(FinanceContext context, Guid a, Guid b, Guid c, Guid d, CancellationToken token) =>
            Task.FromResult(new FinancePartyBalanceTransferOwnerAnswerV1(FinancePartyBalanceTransferOwnerOutcomeV1.Available,
                ChangeDuringRecord && ++changingReads > 1 ? proof with { SuccessorAccountVersion = "2" } : proof));
    }
    private sealed class EvidencePort : IFinancePartyBalanceTransferEvidencePortV1
    {
        public ReadOutcome Outcome { get; set; } = ReadOutcome.Found;
        public Task<ReadOutcome> CheckAsync(FinanceContext context, FinancePartyBalanceTransferOwnerProofV1 owner,
            FinancePartyBalanceTransferEvidenceV1 evidence, CancellationToken token) => Task.FromResult(Outcome);
    }
    private sealed class PeriodPort : IFinanceRecordPeriodRule
    {
        public RecordPeriodOutcome Outcome { get; set; } = RecordPeriodOutcome.Open;
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken token) => Task.FromResult(new RecordPeriodAnswer(Outcome, null));
    }
    private sealed class Collector : IAuditEventCollector
    {
        public List<AuditEventDraft> Events { get; } = [];
        public void Enqueue(AuditEventDraft item) => Events.Add(item);
    }
}
