using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SignedApprovals;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP13 (YOL-D11) against a REAL SQL Server and the REAL host composition: an approved order's signed approval is recorded through the
/// real Sales store in one save (record, attempt, audit event); a second record supersedes the first in the same save; the same key
/// again stores nothing; a stale order version stores nothing; another company never sees the records. The database itself refuses a
/// second active record of an order (UX_OrderSignedApprovals_Active), a changed pinned document, a passive record going back to
/// active and a delete (TR 52261), any change of an attempt (TR 52262), and the migration refuses to roll back over a recorded
/// signed approval (THROW 52260, forward fix). No other module's table is written. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderSignedApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 22);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("5b4f5e6d-7c8b-49aa-8d3e-4f5a6b7c8d01");
    private static readonly Guid MoneyProfile = Guid.Parse("5b4f5e6d-7c8b-49aa-8d3e-4f5a6b7c8d02");
    private static readonly Guid KgProfile = Guid.Parse("5b4f5e6d-7c8b-49aa-8d3e-4f5a6b7c8d03");
    private static readonly Guid Product = Guid.Parse("5b4f5e6d-7c8b-49aa-8d3e-4f5a6b7c8d04");
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Recorder = "SENTETIK-signed-recorder";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Recorded = new("sales.order-signed-approval.recorded.v1", "RecordSignedApproval", "SENTETIK bağlandı");
    private static readonly SalesDraftStateAudit Superseded = new("sales.order-signed-approval.superseded.v1", "SupersedeSignedApproval", "SENTETIK yenilendi");

    [SqlServerFact]
    public async Task A_signed_approval_is_recorded_in_one_save_superseded_by_a_newer_one_and_the_database_keeps_one_active_record_and_every_fact()
    {
        await using var run = await Run.StartAsync();
        var order = await run.ApprovedOrderAsync(CompanyA, "k");
        var auditBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]");

        var first = Approval(order, 1);
        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = await scope.Store.StoreSignedApprovalAsync(run.Envelope("k-signed-1"), Hash("k-signed-1"), order.Version, first,
                Recorded, Superseded);
            Assert.Equal(SalesDraftWriteOutcome.Recorded, stored.Outcome);
        }

        Assert.Equal((1, 1, auditBefore + 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[OrderSignedApprovals] WHERE [OrderId] = @id AND [State] = 1", ("@id", order.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[OrderSignedApprovalAttempts] WHERE [OrderId] = @id", ("@id", order.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]")));

        // The same key again: a replay, nothing new is stored.
        await using (var scope = run.SalesStore(CompanyA))
        {
            var replay = await scope.Store.StoreSignedApprovalAsync(run.Envelope("k-signed-1"), Hash("k-signed-1"), order.Version,
                Approval(order, 1), Recorded, Superseded);
            Assert.Equal(SalesDraftWriteOutcome.Replayed, replay.Outcome);
        }

        // A stale order version: refused before anything is written.
        await using (var scope = run.SalesStore(CompanyA))
        {
            await Assert.ThrowsAsync<SalesVersionConflictException>(() => scope.Store.StoreSignedApprovalAsync(run.Envelope("k-stale"),
                Hash("k-stale"), order.Version - 1, Approval(order, 2, orderVersion: order.Version - 1), Recorded, Superseded));
        }

        // UX_OrderSignedApprovals_Active: a raw second active record of the same order is refused by the index.
        var unique = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(CopyAsActive, ("@id", first.Id)));
        Assert.True(unique.Number is 2601 or 2627);

        // A newer document version supersedes the first in the same save.
        var second = Approval(order, 2);
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.StoreSignedApprovalAsync(run.Envelope("k-signed-2"), Hash("k-signed-2"), order.Version, second, Recorded,
                Superseded);
        }

        Assert.Equal((2, 2L, 2, second.Id), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[OrderSignedApprovals] WHERE [Id] = @id", ("@id", first.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[OrderSignedApprovals] WHERE [Id] = @id", ("@id", first.Id)),
            await run.ScalarAsync<int>("SELECT [DeactivationKind] FROM [sales].[OrderSignedApprovals] WHERE [Id] = @id", ("@id", first.Id)),
            await run.ScalarAsync<Guid>("SELECT [SupersededById] FROM [sales].[OrderSignedApprovals] WHERE [Id] = @id", ("@id", first.Id))));

        const string where = " WHERE [Id] = @id; SELECT 1";
        // TR_OrderSignedApprovals_Guard: the pinned document never changes, a passive record never comes back, nothing is deleted.
        Assert.Equal(52261, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[OrderSignedApprovals] SET [DocumentVersionNo] = 9" + where, ("@id", second.Id)))).Number);
        Assert.Equal(52261, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[OrderSignedApprovals] SET [State] = 1, [Version] = 1, [DeactivationKind] = NULL, [DeactivatedBy] = NULL, "
            + "[DeactivatedAtUtc] = NULL, [SupersededById] = NULL" + where, ("@id", first.Id)))).Number);
        Assert.Equal(52261, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[OrderSignedApprovals]" + where, ("@id", first.Id)))).Number);
        // TR_OrderSignedApprovalAttempts_Guard: the attempts are append-only.
        Assert.Equal(52262, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[OrderSignedApprovalAttempts] SET [ActorId] = N'SENTETIK-other' WHERE [OrderId] = @id; SELECT 1",
            ("@id", order.Id)))).Number);
        Assert.Equal(52262, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[OrderSignedApprovalAttempts] WHERE [OrderId] = @id; SELECT 1", ("@id", order.Id)))).Number);

        // The order row was never written by a signed approval.
        Assert.Equal(order.Version, await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", order.Id)));

        // Another company neither finds the order nor lists its records.
        await using (var other = run.SalesStore(CompanyB))
        {
            Assert.Null(await other.Store.FindSignedApprovalOrderAsync(order.Id));
            Assert.Empty(await other.Store.ListSignedApprovalsAsync(order.Id, SalesOrderSignedApprovalApiV1.MaximumListed));
        }

        await using (var own = run.SalesStore(CompanyA))
        {
            var history = await own.Store.ListSignedApprovalsAsync(order.Id, SalesOrderSignedApprovalApiV1.MaximumListed);
            Assert.Equal(2, history.Count);
            Assert.Single(history, item => item.State == SalesOrderSignedApprovalState.Active);
        }
    }

    [SqlServerFact]
    public async Task The_signed_approval_migrations_Down_passes_with_no_record_and_refuses_52260_while_one_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesOrderSignedApprovals().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");

        var order = await run.ApprovedOrderAsync(CompanyA, "k");
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.StoreSignedApprovalAsync(run.Envelope("k-signed"), Hash("k-signed"), order.Version, Approval(order, 1),
                Recorded, Superseded);
        }

        Assert.Equal(52260, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[OrderSignedApprovals]"));
    }

    /// <summary>A raw copy of the stored record under another id, still active (the unique filtered index refuses it).</summary>
    private const string CopyAsActive =
        "INSERT INTO [sales].[OrderSignedApprovals] ([Id],[CompanyId],[OrderId],[OrderRevision],[OrderVersion],[DocumentId],"
        + "[DocumentVersionNo],[DocumentSha256],[State],[Version],[RecordedBy],[RecordedAtUtc],[CorrelationId]) SELECT NEWID(),[CompanyId],"
        + "[OrderId],[OrderRevision],[OrderVersion],[DocumentId],[DocumentVersionNo] + 1,[DocumentSha256],1,1,[RecordedBy],[RecordedAtUtc],"
        + "[CorrelationId] FROM [sales].[OrderSignedApprovals] WHERE [Id] = @id; SELECT 1";

    private static SalesOrderSignedApproval Approval(SalesOrderDraft order, int documentVersion, long? orderVersion = null) =>
        SalesOrderSignedApproval.Record(Guid.NewGuid(), order.CompanyId, order.Id, order.Revision, orderVersion ?? order.Version,
            new SalesOrderSignedApprovalDocument(Guid.Parse("d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0"), documentVersion, Sha), Recorder, T0);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line 10 kg draft from SYNTHETIC owner snapshots in the kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0, customerAcceptanceRequired: true);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "10", 10m, Day.AddDays(3)), T0);
        return draft;
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public SalesStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-wp13-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored order of the company through the real Sales store: Rev.0 submitted and approved inside — version 4.</summary>
        public async Task<SalesOrderDraft> ApprovedOrderAsync(Guid company, string keyPrefix)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(Envelope($"{keyPrefix}-create", Actor), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            await scope.Store.UpdateDraftStateAsync(Envelope($"{keyPrefix}-submit", Actor), Hash($"{keyPrefix}-submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(Envelope($"{keyPrefix}-approve", "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve"), SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now),
                Approved);
            Assert.Equal((4L, 0, InternalApprovalState.Approved), (approved!.Draft.Version, approved.Draft.Revision, approved.Draft.ApprovalState));
            return approved.Draft;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string actor = Recorder) =>
            new(actor, SalesPermissionManifestV1.OrdersSignedApprovalRecord, ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1",
                "SENTETIK-agent");

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
