using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL source coverage for the owner read; synthetic parties, authority and receipt seed.
/// Admission and allocation/release use the actual Finance commands. No bank reconciliation is seeded.</summary>
public sealed class FinanceSalesInvoiceSettlementSqlServerTests
{
    [SqlServerFact]
    public async Task Entire_admitted_invoice_settles_only_after_both_allocations_and_reopens_on_release()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice();
        await run.Admit(invoice);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, (await run.Read(invoice.InvoiceId)).Status);
        var sourceId = await run.SeedRecordedReceipt();
        var first = await run.Allocate(sourceId, invoice.Installments[0].OpenItemId, "1");
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, (await run.Read(invoice.InvoiceId)).Status);
        var second = await run.Allocate(sourceId, invoice.Installments[1].OpenItemId, "1");
        Assert.Equal(CommandOutcome.Succeeded, second.Outcome);
        var settled = await run.Read(invoice.InvoiceId);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, settled.Status);
        Assert.Equal(invoice.AdmissionId, settled.ScopeId);
        Assert.Equal(invoice.InvoiceVersion, settled.SourceVersion);
        var released = await run.Allocator().ReleaseAsync(run.Context, new("SENTETIK-release"),
            new(first.Resource!.Id, "1", "2", "SENTETIK-release"), CancellationToken.None);
        Assert.Equal(CommandOutcome.Succeeded, released.Outcome);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, (await run.Read(invoice.InvoiceId)).Status);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Unknown, (await run.Read(Guid.NewGuid())).Status);
    }

    [SqlServerFact]
    public async Task Company_actor_and_lost_consumer_authority_do_not_release_any_invoice_evidence()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice();
        await run.Admit(invoice);
        var reader = run.Reader();
        var mismatched = await reader.ReadAsync(run.Context with { CompanyId = TestCompanyIds.CompanyB },
            [invoice.InvoiceId], _ => Task.CompletedTask);
        Assert.Equal(ReadOutcome.Denied, mismatched.Outcome);
        Assert.Null(mismatched.Resource);
        var calls = 0;
        var revoked = await reader.ReadAsync(run.Context, [invoice.InvoiceId], _ =>
        {
            if (++calls == 3) throw new UnauthorizedAccessException(); // after the first SQL read
            return Task.CompletedTask;
        });
        Assert.Equal(ReadOutcome.Unavailable, revoked.Outcome);
        Assert.Null(revoked.Resource);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Party = Guid.Parse("fdad9c97-099d-4d4d-81e3-3cfe916706a1");
    private static readonly Guid Account = Guid.Parse("fdad9c97-099d-4d4d-81e3-3cfe916706a2");
    private static readonly Guid Currency = Guid.Parse("fdad9c97-099d-4d4d-81e3-3cfe916706a3");
    private static readonly Guid Treasury = Guid.Parse("fdad9c97-099d-4d4d-81e3-3cfe916706a4");
    private const string Actor = "SENTETIK-settlement";
    private static FinanceInvoiceOpenItemAdmissionV1 Invoice() => new(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales,
        Guid.NewGuid(), "invoice:v4", Party, Account, Currency, null,
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2027, 1, 1), "10", null),
         new(Guid.NewGuid(), Guid.NewGuid(), 2, new(2027, 2, 1), "10", null)]);

    private sealed class Run(SqlServerRelationalApiFactory factory, string baseConnection) : IAsyncDisposable
    {
        private readonly CompanyContext company = new();
        private readonly User user = new();
        public FinanceContext Context { get; } = new(Company, Actor, "SENTETIK-settlement-correlation", null);
        public FinanceDbContext Db { get; private set; } = null!;
        public static async Task<Run> StartAsync()
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(connection);
            var run = new Run(factory, connection);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            run.company.Set(Company);
            run.Db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options, run.company);
            await run.Db.Database.MigrateAsync();
            return run;
        }
        public async Task Admit(FinanceInvoiceOpenItemAdmissionV1 invoice)
        {
            var service = new FinanceInvoiceOpenItemAdmissionService(Db, company, user, new PartyPort(), new Authority(),
                new AuditEventCollector(), new("SENTETIK-admit", ["SENTETIK"], "Web"), TimeProvider.System);
            var result = await service.AdmitAsync(Context, new("SENTETIK-admit"), invoice, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Db.ChangeTracker.Clear();
        }
        public FinanceSalesInvoiceSettlementReaderV1 Reader() => new(Db, company, user);
        public async Task<FinanceSalesInvoiceSettlementV1> Read(Guid invoiceId)
        {
            var result = await Reader().ReadAsync(Context, [invoiceId], _ => Task.CompletedTask);
            Assert.Equal(ReadOutcome.Found, result.Outcome);
            Assert.Equal(Company, result.Resource!.CompanyId);
            Assert.False(Db.ChangeTracker.HasChanges());
            return Assert.Single(result.Resource.Invoices);
        }
        public FinanceAllocationService Allocator() => new(Db, company, user, new Permissions(), new AuditEventCollector(),
            new("SENTETIK-allocate", ["SENTETIK"], "Web"), TimeProvider.System);
        public async Task<FinanceAllocationCommandResult> Allocate(Guid sourceId, Guid itemId, string sourceVersion)
        {
            var result = await Allocator().CreateAsync(Context, new($"SENTETIK-{itemId:N}"),
                new(Guid.NewGuid(), sourceId, sourceVersion, itemId, "1", "10", "SENTETIK"), CancellationToken.None);
            Db.ChangeTracker.Clear();
            return result;
        }
        public async Task<Guid> SeedRecordedReceipt()
        {
            var id = Guid.NewGuid();
            var now = TimeProvider.System.GetUtcNow();
            Db.ReceiptDrafts.Add(new()
            {
                Id = id, CompanyId = Company, Version = 1, PartyId = Party, CompanyAccountId = Account,
                CurrencyId = Currency, TreasuryAccountId = Treasury, AmountText = "20", ProfileId = "SENTETIK",
                ProfileVersion = "v1", OccurredOn = new(2026, 11, 27), Stage = ReceiptStages.Recorded,
                CreatedAtUtc = now, UpdatedAtUtc = now, RecordedAtUtc = now, CreatedBy = Actor, UpdatedBy = Actor, RecordedBy = Actor,
            });
            Db.MoneyEvents.Add(new()
            {
                Id = Guid.NewGuid(), CompanyId = Company, SourceId = id, Kind = MoneyEventKinds.ManualBankReceiptRecorded,
                PartyId = Party, CompanyAccountId = Account, CurrencyId = Currency, TreasuryAccountId = Treasury,
                AmountText = "20", ProfileId = "SENTETIK", ProfileVersion = "v1", OccurredOn = new(2026, 11, 27),
                RecordedAtUtc = now, RecordedBy = Actor, CorrelationId = "SENTETIK-seed",
            });
            await Db.SaveChangesAsync(); Db.ChangeTracker.Clear();
            return id;
        }
        public async ValueTask DisposeAsync()
        {
            if (Db is not null) await Db.DisposeAsync();
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class PartyPort : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) => Task.FromResult(
                new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new(Party, Account, Company, true, IsPartyActive: true)));
    }
    private sealed class Authority : IFinanceInvoiceOpenItemAdmissionAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
    private sealed class Permissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
}
