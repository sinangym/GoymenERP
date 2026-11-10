using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-07 (EK21 SAT-D153…D162; Sinan 2026-11-10, Issue #66 / EK55 pending) against a REAL SQL Server and the REAL host
/// composition: the REAL risk exception service over the real Sales store requests a single-shipment exception in one save
/// (exception, attempt, audit; the order is unchanged), replays the same key, refuses the requester's own approval as the key's
/// recorded 409, and another actor's approval is valid on its Europe/Istanbul business date only (the read port answers Valid,
/// then Lapsed the next day). The database itself refuses a self-decision (CK maker-checker), a second pending exception of the
/// same shipment (filtered unique index), a change of the request facts or a second decision (TR 51252) and any change of an
/// attempt (TR 51253). Another company neither sees nor decides an exception; the migration refuses to roll back over a recorded
/// exception (THROW 51251, forward fix). SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesShipmentRiskExceptionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 10);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("6c9a2e33-7f4d-4e0a-9b32-4d5e6f7a8c01");
    private static readonly Guid MoneyProfile = Guid.Parse("6c9a2e33-7f4d-4e0a-9b32-4d5e6f7a8c02");
    private static readonly Guid KgProfile = Guid.Parse("6c9a2e33-7f4d-4e0a-9b32-4d5e6f7a8c03");
    private static readonly Guid Product = Guid.Parse("6c9a2e33-7f4d-4e0a-9b32-4d5e6f7a8c04");
    private static readonly Guid Shipment = Guid.Parse("6c9a2e33-7f4d-4e0a-9b32-4d5e6f7a8c05");
    private const string Shipper = "SENTETIK-shipper";
    private const string Manager = "SENTETIK-manager";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task A_shipment_risk_exception_is_requested_in_one_save_decided_by_another_person_and_valid_on_its_istanbul_day_only_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        // 20:30 UTC = 23:30 Istanbul on 10 Nov: the approval's business date is 10 Nov.
        var clock = new Clock(new DateTimeOffset(2026, 11, 10, 20, 30, 0, TimeSpan.Zero));
        await using var scope = run.SalesStore(CompanyA, clock);
        var shipper = run.Service(scope.Store, CompanyA, Shipper, clock);

        var requested = await shipper.RequestRiskExceptionAsync(draft.Id,
            new RequestSalesRiskExceptionRequest(Shipment, "SENTETIK müşteri ödeme sözü verdi"), Metadata("k-request"));
        Assert.Equal((1, "Requested", "1", false), (requested.Exception.ExceptionNo, requested.Exception.State, requested.Exception.Version,
            requested.IsReplay));
        // One save: the exception, its attempt and its audit; the order keeps its version and opens no revision (SAT-D158).
        Assert.Equal((1, 1, 4L, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptionAttempts] WHERE [OrderId] = @id AND [Outcome] = 1",
                ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.ShipmentRiskException' "
            + "AND [EventType] = N'sales.order.risk-exception.requested.v1' AND [ActorId] = @actor AND [AfterJson] LIKE N'%' + @shipment + N'%'",
            ("@actor", Shipper), ("@shipment", Shipment.ToString("D"))));

        // The same key replays and writes nothing again.
        var replay = await shipper.RequestRiskExceptionAsync(draft.Id,
            new RequestSalesRiskExceptionRequest(Shipment, "SENTETIK müşteri ödeme sözü verdi"), Metadata("k-request"));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions]"));

        // The requester's own approval is refused and recorded under its key (SAT-D153); nothing moves.
        var self = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            shipper.ApproveRiskExceptionAsync(draft.Id, 1, new SalesDraftStateCommandRequest("1"), Metadata("k-self")));
        Assert.Equal((409, SalesRiskExceptionProblemCodesV1.SelfDecision), (self.Status, self.Code));
        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptionAttempts] WHERE [Outcome] = 2")));

        // Another actor approves: valid on 10 Nov (Istanbul) only.
        var manager = run.Service(scope.Store, CompanyA, Manager, clock);
        var approved = await manager.ApproveRiskExceptionAsync(draft.Id, 1, new SalesDraftStateCommandRequest("1"), Metadata("k-approve"));
        Assert.Equal(("Approved", "2", (DateOnly?)Day, true), (approved.Exception.State, approved.Exception.Version,
            approved.Exception.ValidOn, approved.Exception.IsValidToday));
        Assert.Equal((Manager, Shipper), (
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [RequestedBy] FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order.risk-exception.approved.v1' "
            + "AND [ActorId] = @actor AND [EntityVersionBefore] = 1 AND [EntityVersionAfter] = 2", ("@actor", Manager)));

        var port = new SalesShipmentRiskExceptionReadPort(scope.Store, run.Company(CompanyA), clock);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Valid, (int?)1), Answer(await port.GetAsync(draft.Id, Shipment)));
        // 21:30 UTC = 00:30 Istanbul on 11 Nov (still 10 Nov in UTC): the approval has lapsed and the gate holds the shipment again.
        clock.Now = clock.Now.AddHours(1);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Lapsed, (int?)1), Answer(await port.GetAsync(draft.Id, Shipment)));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_self_decision_a_second_pending_request_a_changed_request_and_any_attempt_change()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var clock = new Clock(T0);
        await using (var scope = run.SalesStore(CompanyA, clock))
        {
            await run.Service(scope.Store, CompanyA, Shipper, clock).RequestRiskExceptionAsync(draft.Id,
                new RequestSalesRiskExceptionRequest(Shipment, "SENTETIK"), Metadata("k-request"));
        }

        const string where = " WHERE [OrderId] = @id; SELECT 1";
        // CK_ShipmentRiskExceptions_MakerChecker: the decider is never the requester.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ShipmentRiskExceptions] SET [State] = 2, [DecidedBy] = [RequestedBy], [DecidedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[ValidOn] = '2026-11-10', [Version] = 2" + where, ("@id", draft.Id)))).Number);
        // UX_ShipmentRiskExceptions_PendingShipment: one pending exception per shipment of an order.
        Assert.Equal(2601, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [sales].[ShipmentRiskExceptions] ([Id],[CompanyId],[OrderId],[ExceptionNo],[LoadingInstructionId],[Reason],[State],"
            + "[RequestedBy],[RequestedAtUtc],[Version],[UpdatedAtUtc],[CorrelationId]) VALUES (NEWID(), @company, @id, 2, @shipment, N'x', 1, "
            + "N'SENTETIK-other', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), N'x'); SELECT 1",
            ("@company", CompanyA), ("@id", draft.Id), ("@shipment", Shipment)))).Number);
        // TR_ShipmentRiskExceptions_Guard: the request facts never change.
        Assert.Equal(51252, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ShipmentRiskExceptions] SET [Reason] = N'SENTETIK başka'" + where, ("@id", draft.Id)))).Number);
        // FK_ShipmentRiskExceptionAttempts_ShipmentRiskExceptions_CompanyId_ExceptionId: the recorded attempt keeps its exception, and
        // the foreign key is checked before the AFTER trigger runs (the trigger's own DELETE refusal is proven below on an unreferenced row).
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[ShipmentRiskExceptions]" + where, ("@id", draft.Id)))).Number);
        // A valid decision passes once; a second decision is refused by the trigger.
        await run.ScalarAsync<int>("UPDATE [sales].[ShipmentRiskExceptions] SET [State] = 3, [DecidedBy] = N'SENTETIK-manager', "
            + "[DecidedAtUtc] = SYSDATETIMEOFFSET(), [RejectionReason] = N'SENTETIK', [Version] = 2" + where, ("@id", draft.Id));
        Assert.Equal(51252, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ShipmentRiskExceptions] SET [State] = 2, [RejectionReason] = NULL, [ValidOn] = '2026-11-10', [Version] = 3" + where,
            ("@id", draft.Id)))).Number);
        // TR_ShipmentRiskExceptionAttempts_Guard: the attempts are append-only.
        Assert.Equal(51253, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ShipmentRiskExceptionAttempts] SET [ActorId] = N'SENTETIK-other'" + where, ("@id", draft.Id)))).Number);
        Assert.Equal(51253, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[ShipmentRiskExceptionAttempts]" + where, ("@id", draft.Id)))).Number);
        Assert.Equal((3, 1), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptionAttempts] WHERE [OrderId] = @id", ("@id", draft.Id))));
        // TR_ShipmentRiskExceptions_Guard: nothing is deleted, even a pending row no attempt references (written raw for another
        // shipment of the order, so neither the foreign key nor a decided state stands in front of the trigger).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "INSERT INTO [sales].[ShipmentRiskExceptions] ([Id],[CompanyId],[OrderId],[ExceptionNo],[LoadingInstructionId],[Reason],[State],"
            + "[RequestedBy],[RequestedAtUtc],[Version],[UpdatedAtUtc],[CorrelationId]) VALUES (NEWID(), @company, @id, 2, NEWID(), N'x', 1, "
            + "N'SENTETIK-other', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), N'x'); SELECT 1",
            ("@company", CompanyA), ("@id", draft.Id)));
        Assert.Equal(51252, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id AND [ExceptionNo] = 2; SELECT 1", ("@id", draft.Id)))).Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task Another_company_neither_sees_nor_requests_nor_decides_a_risk_exception_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var clock = new Clock(T0);
        await using (var scope = run.SalesStore(CompanyA, clock))
        {
            await run.Service(scope.Store, CompanyA, Shipper, clock).RequestRiskExceptionAsync(draft.Id,
                new RequestSalesRiskExceptionRequest(Shipment, "SENTETIK"), Metadata("k-request"));
        }

        await using (var other = run.SalesStore(CompanyB, clock))
        {
            var foreign = run.Service(other.Store, CompanyB, Manager, clock);
            Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                foreign.ApproveRiskExceptionAsync(draft.Id, 1, new SalesDraftStateCommandRequest("1"), Metadata("k-foreign-approve")))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => foreign.RequestRiskExceptionAsync(draft.Id,
                new RequestSalesRiskExceptionRequest(Shipment, "SENTETIK"), Metadata("k-foreign-request")))).Status);
            Assert.Empty(await other.Store.ListRiskExceptionsAsync(draft.Id, SalesRiskExceptionApiV1.MaximumListed));
            var port = new SalesShipmentRiskExceptionReadPort(other.Store, run.Company(CompanyB), clock);
            Assert.Equal(SalesShipmentRiskExceptionStatusV1.None, (await port.GetAsync(draft.Id, Shipment)).Status);
        }

        Assert.Equal((1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions]"),
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptionAttempts]")));
    }

    [SqlServerFact]
    public async Task The_risk_exception_migrations_Down_passes_with_no_exception_and_refuses_51251_while_one_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var guard = Assert.IsType<SqlOperation>(new AddShipmentRiskExceptions().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");

        var clock = new Clock(T0);
        await using (var scope = run.SalesStore(CompanyA, clock))
        {
            await run.Service(scope.Store, CompanyA, Shipper, clock).RequestRiskExceptionAsync(draft.Id,
                new RequestSalesRiskExceptionRequest(Shipment, "SENTETIK"), Metadata("k-request"));
        }

        Assert.Equal(51251, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, id => id.EndsWith("_AddShipmentRiskExceptions", StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[index]);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[index - 1]));
        Assert.Equal(51251, rollback.Number);
        Assert.Equal(applied[..(index + 1)], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions] WHERE [OrderId] = @id", ("@id", draft.Id)));
    }

    private static (SalesShipmentRiskExceptionStatusV1, int?) Answer(SalesShipmentRiskExceptionReadV1 read) => (read.Status, read.ExceptionNo);

    private static SalesCommandMetadata Metadata(string key) =>
        new(key, "sales-risk-exception-" + key, null, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "100", 100m, Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>The SYNTHETIC grants: both risk exception permissions (never `sales.drafts.read`, which the commands do not need).</summary>
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission is SalesPermissionManifestV1.OrdersRiskExceptionRequest or SalesPermissionManifestV1.OrdersRiskExceptionApprove
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-SALES"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
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
        public string Correlation { get; } = $"sales-api-07-{Guid.NewGuid():N}";

        /// <summary>The real host composed over a fresh database (health is probed so the migrations have run).</summary>
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

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix)
        {
            await using var scope = SalesStore(company, TimeProvider.System);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public CompanyContext Company(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return companyContext;
        }

        /// <summary>The REAL risk exception service over a real store as the given SYNTHETIC actor.</summary>
        public SalesRiskExceptionService Service(SalesStore store, Guid company, string actor, TimeProvider clock) =>
            new(Company(company), new FixedUser(actor, company), new Authority(), new FixedCorrelation(Correlation), store, store, clock);

        public SalesDbContext SalesContext(Guid company) =>
            new(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, Company(company));

        public SalesScope SalesStore(Guid company, TimeProvider clock)
        {
            var companyContext = Company(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, clock));
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
