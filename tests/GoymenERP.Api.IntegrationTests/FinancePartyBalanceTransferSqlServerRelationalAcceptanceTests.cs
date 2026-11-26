using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL storage/atomicity; owner, permission and open-period inputs are SENTETIK / NOT_ENV, not grants.</summary>
public sealed class FinancePartyBalanceTransferSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid OtherCompany = TestCompanyIds.CompanyB;
    private static readonly Guid Party = Guid.Parse("f3840000-0000-4000-8000-000000000001");
    private static readonly Guid Account = Guid.Parse("f3840000-0000-4000-8000-000000000002");
    private static readonly Guid Successor = Guid.Parse("f3840000-0000-4000-8000-000000000003");
    private static readonly Guid SuccessorAccount = Guid.Parse("f3840000-0000-4000-8000-000000000004");
    private static readonly Guid Currency = Guid.Parse("f3840000-0000-4000-8000-000000000005");
    private const string Actor = "SENTETIK-balance-transfer";

    [SqlServerFact]
    public async Task Full_balance_and_allocation_positions_survive_reopen_replay_and_other_company_reads()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddFinancePartyBalanceTransfers().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var itemId = await run.SeedItemAsync("100.0100");
        var receiptId = await run.SeedReceiptAsync("40.0100");
        RecordFinancePartyBalanceTransferV1 request;
        await using (var scope = run.Open())
        {
            var preview = (await scope.Service.PreviewAuthorizedAsync(scope.Context, Selection(), default)).Preview!;
            Assert.Equal("60.0000", Assert.Single(preview.Currencies).Amount);
            request = Request(preview);
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Service.RecordAsync(scope.Context, new("SENTETIK-transfer"), request, default)).Outcome);
        }
        await using (var reopened = run.Open())
        {
            var read = await reopened.Service.GetAuthorizedAsync(reopened.Context, request.TransferId, default);
            Assert.Equal(ReadOutcome.Found, read.Outcome);
            Assert.Equal("60.0000", Assert.Single(read.Transfer!.Currencies).Amount);
            var replay = await reopened.Service.RecordAsync(reopened.Context with { CorrelationId = "SENTETIK-reopened-retry" },
                new("SENTETIK-transfer"), request, default);
            Assert.True(replay.Replayed);
            Assert.Equal("SENTETIK-transfer-correlation", replay.OriginalCorrelationId);
            var source = await reopened.Db.OpenItems.AsNoTracking().SingleAsync(x => x.Id == itemId);
            Assert.Equal((Party, Account, "100.0100", "100.0100", 2L),
                (source.PartyId, source.CompanyAccountId, source.OriginalAmountText, source.OutstandingAmountText, source.Version));
            var rows = await reopened.Db.PartyBalanceAssignments.AsNoTracking().ToArrayAsync();
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row => Assert.Equal(SuccessorAccount, row.CurrentAccountId));
        }
        await using (var foreign = run.Open(OtherCompany))
        {
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.GetAuthorizedAsync(foreign.Context, request.TransferId, default)).Outcome);
            Assert.Empty(await foreign.Db.PartyBalanceAssignments.ToListAsync());
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.party-balance-transfer.recorded.v1'"));
        Assert.Equal(51984, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceTransfers"));

        // A stale pre-transfer allocation cannot spend either the old or the newly assigned position.
        await using (var stale = run.Open())
        {
            var refused = await stale.Allocations.CreateAsync(stale.Context, new("SENTETIK-stale"),
                new(Guid.NewGuid(), receiptId, "2", itemId, "1", "10.0000", "SENTETIK stale", Successor, SuccessorAccount), default);
            Assert.Equal(CommandOutcome.VersionConflict, refused.Outcome);
        }
        Guid allocationId;
        await using (var allocate = run.Open())
        {
            allocationId = Guid.NewGuid();
            var made = await allocate.Allocations.CreateAsync(allocate.Context, new("SENTETIK-allocate"),
                new(allocationId, receiptId, "2", itemId, "2", "40.0100", "SENTETIK allocation", Successor, SuccessorAccount), default);
            Assert.Equal(CommandOutcome.Succeeded, made.Outcome);
            Assert.Equal(SuccessorAccount, made.Resource!.AllocationAccountId);
        }
        await using (var release = run.Open())
        {
            Assert.Equal(CommandOutcome.Succeeded, (await release.Allocations.ReleaseAsync(release.Context, new("SENTETIK-release"),
                new(allocationId, "1", "3", "SENTETIK release"), default)).Outcome);
            Assert.Equal("100.0100", (await release.Db.PartyBalanceAssignments.AsNoTracking()
                .SingleAsync(x => x.SourceId == itemId)).AvailableAmountText);
            Assert.Equal("40.0100", (await release.Db.PartyBalanceAssignments.AsNoTracking()
                .SingleAsync(x => x.SourceId == receiptId)).AvailableAmountText);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task Concurrent_full_balance_transfers_cannot_move_the_same_source_twice()
    {
        await using var run = await Run.StartAsync();
        await run.SeedItemAsync("100.00");
        RecordFinancePartyBalanceTransferV1 request;
        await using (var preview = run.Open())
            request = Request((await preview.Service.PreviewAuthorizedAsync(preview.Context, Selection(), default)).Preview!);
        await using var first = run.Open(); await using var second = run.Open();
        var results = await Task.WhenAll(
            first.Service.RecordAsync(first.Context, new("SENTETIK-race-a"), request, default),
            second.Service.RecordAsync(second.Context, new("SENTETIK-race-b"), request with { TransferId = Guid.NewGuid() }, default));
        Assert.Single(results, x => x.Outcome == CommandOutcome.Succeeded);
        Assert.Contains(results, x => x.Outcome is CommandOutcome.VersionConflict or CommandOutcome.Unavailable);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceAssignments"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.party-balance-transfer.recorded.v1'"));
    }

    [SqlServerFact]
    public async Task Late_original_difference_requires_a_new_explicit_transfer_and_audit_failure_rolls_everything_back()
    {
        await using var run = await Run.StartAsync();
        await run.SeedItemAsync("100.00");
        await using (var first = run.Open())
        {
            var preview = (await first.Service.PreviewAuthorizedAsync(first.Context, Selection(), default)).Preview!;
            Assert.Equal(CommandOutcome.Succeeded, (await first.Service.RecordAsync(first.Context, new("SENTETIK-first"), Request(preview), default)).Outcome);
        }
        var correction = await run.SeedItemAsync("20.00");
        await using (var later = run.Open())
        {
            Assert.False(await later.Db.PartyBalanceAssignments.AnyAsync(x => x.SourceId == correction));
            var old = await later.Ledger.SummarizeAuthorizedAsync(later.Context, new(Party, Account), default);
            var next = await later.Ledger.SummarizeAuthorizedAsync(later.Context, new(Successor, SuccessorAccount), default);
            Assert.Equal("20.00", Assert.Single(old.Balance!.Currencies).Balance.Amount.Money!.Amount);
            Assert.Equal("100.00", Assert.Single(next.Balance!.Currencies).Balance.Amount.Money!.Amount);
        }
        await run.ExecuteAsync("""
            CREATE TRIGGER audit.SyntheticBalanceTransferFailure ON audit.AuditEvents AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE EventType = N'finance.party-balance-transfer.recorded.v1')
                    RAISERROR ('SENTETIK audit failure', 16, 1);
            END;
            """);
        RecordFinancePartyBalanceTransferV1 secondRequest;
        await using (var failing = run.Open())
        {
            var preview = (await failing.Service.PreviewAuthorizedAsync(failing.Context, Selection(), default)).Preview!;
            secondRequest = Request(preview);
            Assert.Equal(CommandOutcome.Unavailable,
                (await failing.Service.RecordAsync(failing.Context, new("SENTETIK-second"), secondRequest, default)).Outcome);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceTransferCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyBalanceAssignments"));
        await run.ExecuteAsync("DROP TRIGGER audit.SyntheticBalanceTransferFailure;");
        await using (var retry = run.Open())
        {
            Assert.Equal(CommandOutcome.Succeeded,
                (await retry.Service.RecordAsync(retry.Context, new("SENTETIK-second"), secondRequest, default)).Outcome);
            var next = await retry.Ledger.SummarizeAuthorizedAsync(retry.Context, new(Successor, SuccessorAccount), default);
            Assert.Equal("120.00", Assert.Single(next.Balance!.Currencies).Balance.Amount.Money!.Amount);
        }
    }

    private static FinancePartyBalanceTransferSelectionV1 Selection() => new(Party, Account, Successor, SuccessorAccount);
    private static RecordFinancePartyBalanceTransferV1 Request(FinancePartyBalanceTransferPreviewV1 preview) => new(Guid.NewGuid(),
        Selection(), preview.SnapshotVersion, new(2026, 11, 17), "SENTETIK legal transfer", new(Guid.NewGuid(), 1, new string('a', 64)));
    private sealed class Run(string baseConnection, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(connection);
            var run = new Run(connection, factory);
            using var client = factory.CreateClient(); using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
            await using var scope = run.Open(); await scope.Db.Database.MigrateAsync(); return run;
        }
        public Scope Open(Guid? companyId = null)
        {
            var company = new CompanyContext(); company.Set(companyId ?? Company);
            var collector = new AuditEventCollector(); var user = new User(company.CompanyId); var permissions = new Permissions();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options;
            var db = new FinanceDbContext(options, company); var ledger = new PartyLedgerService(db, company, user, permissions);
            var context = new FinanceContext(company.CompanyId, Actor, "SENTETIK-transfer-correlation", null);
            var service = new FinancePartyBalanceTransferServiceV1(db, company, user, permissions, new Owner(), new Evidence(),
                ledger, new Period(), collector, new(FinancePartyBalanceTransferPermissionsV1.Record, ["SENTETIK"], "Web"), TimeProvider.System);
            return new(db, context, service, ledger, new(db, company, user, permissions, collector,
                new(FinancePermissionNames.AllocationWrite, ["SENTETIK"], "Web"), TimeProvider.System));
        }
        public async Task<Guid> SeedItemAsync(string amount)
        {
            await using var scope = Open(); var id = Guid.NewGuid(); var batch = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            scope.Db.OpenItemSourceBatches.Add(new() { Id = batch, CompanyId = Company,
                SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, SourceVersion = "1", SourceChecksum = new string('a', 64),
                IdempotencyKeyHash = Guid.NewGuid().ToString("N").PadRight(64, 'a'), PayloadHash = new string('b', 64), ActorId = Actor,
                ResultJson = "{}", CorrelationId = "SENTETIK seed", AdmittedAtUtc = now });
            scope.Db.OpenItems.Add(new() { Id = id, CompanyId = Company, Direction = FinanceOpenItemDirectionCodes.Receivable,
                SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, SourceId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
                SourceVersion = "1", SourceBatchId = batch, SourceBatchVersion = "1", SourceBatchChecksum = new string('a', 64),
                PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency, OriginalAmountText = amount,
                OutstandingAmountText = amount, Status = FinanceOpenItemStatuses.Open, Version = 1,
                CreatedBy = Actor, UpdatedBy = Actor, CreatedAtUtc = now, UpdatedAtUtc = now });
            await scope.Db.SaveChangesAsync(); return id;
        }
        public async Task<Guid> SeedReceiptAsync(string amount)
        {
            await using var scope = Open(); var id = Guid.NewGuid(); var treasury = Guid.NewGuid(); var profile = Guid.NewGuid().ToString("D");
            var now = DateTimeOffset.UtcNow; var date = new DateOnly(2026, 11, 17);
            scope.Db.ReceiptDrafts.Add(new() { Id = id, CompanyId = Company, Version = 2, PartyId = Party, CompanyAccountId = Account,
                TreasuryAccountId = treasury, CurrencyId = Currency, AmountText = amount, ProfileId = profile, ProfileVersion = "1",
                OccurredOn = date, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = Actor, UpdatedBy = Actor,
                Stage = ReceiptStages.Recorded, RecordedAtUtc = now, RecordedBy = Actor });
            scope.Db.MoneyEvents.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, SourceId = id, Kind = MoneyEventKinds.ManualBankReceiptRecorded,
                PartyId = Party, CompanyAccountId = Account, TreasuryAccountId = treasury, CurrencyId = Currency, AmountText = amount,
                ProfileId = profile, ProfileVersion = "1", OccurredOn = date, RecordedAtUtc = now, RecordedBy = Actor, CorrelationId = "SENTETIK seed" });
            await scope.Db.SaveChangesAsync(); return id;
        }
        public async Task<T> ScalarAsync<T>(string sql)
        { await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
          await using var command = new SqlCommand(sql, connection); return (T)(await command.ExecuteScalarAsync())!; }
        public async Task ExecuteAsync(string sql)
        { await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
          await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync(); SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnection); await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed record Scope(FinanceDbContext Db, FinanceContext Context, FinancePartyBalanceTransferServiceV1 Service,
        PartyLedgerService Ledger, FinanceAllocationService Allocations) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Db.DisposeAsync(); }
    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid value) => value == company;
    }
    private sealed class Permissions : IFinancePermissionPort
    { public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token) => Task.FromResult(PermissionDecision.Allowed); }
    private sealed class Owner : IFinancePartyBalanceTransferOwnerPortV1
    {
        public Task<FinancePartyBalanceTransferOwnerAnswerV1> ReadAsync(FinanceContext context, Guid a, Guid b, Guid c, Guid d, CancellationToken token) => Task.FromResult(
            new FinancePartyBalanceTransferOwnerAnswerV1(FinancePartyBalanceTransferOwnerOutcomeV1.Available,
                new(context.CompanyId, Party, Account, "2", "2", "SENTETIK old", "SENTETIK old party", Successor, SuccessorAccount,
                    "1", "1", "SENTETIK new", "SENTETIK new party", FinancePartyBalanceTransferLinkKindV1.Succession,
                    new(2026, 11, 17), "SENTETIK basis", DateTimeOffset.UnixEpoch)));
    }
    private sealed class Evidence : IFinancePartyBalanceTransferEvidencePortV1
    { public Task<ReadOutcome> CheckAsync(FinanceContext context, FinancePartyBalanceTransferOwnerProofV1 owner, FinancePartyBalanceTransferEvidenceV1 evidence, CancellationToken token) => Task.FromResult(ReadOutcome.Found); }
    private sealed class Period : IFinanceRecordPeriodRule
    { public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn, DateTimeOffset recordedAtUtc, CancellationToken token) => Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null)); }
}
