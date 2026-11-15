using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-018 / plan S2 (FNS-D30, SAT-D310) against a REAL SQL Server and the REAL host composition: the shipment risk gate read port the
/// Logistics gate consumes is resolved from the host. Without a resolved company it throws before any read. Under the order's
/// company, a customer without a credit limit is never "within": the answer is Unavailable with its hold reason, the shipment's
/// single-shipment risk exception is embedded as the exception port answers it (None, then Pending once one is requested), and
/// the read writes nothing. Another company — and an unknown order — answers SourceUnavailable with no exception, never a 403 and
/// never "within". SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesShipmentRiskGateSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 10);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 7, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    // The order is in the local currency (TL), so the risk evaluation needs no Settings money profile or exchange rate of its own.
    private static readonly Guid Currency = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid MoneyProfile = Guid.NewGuid();
    private static readonly Guid KgProfile = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Shipment = Guid.NewGuid();

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task F018_S2_the_host_gate_port_without_a_resolved_company_throws_and_reads_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");

        await using var hostScope = run.Factory.Services.CreateAsyncScope();
        var port = hostScope.ServiceProvider.GetRequiredService<ISalesShipmentRiskGateReadPortV1>();
        Assert.IsType<SalesShipmentRiskGateReadPort>(port);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.GetAsync(draft.Id, Shipment));
    }

    [SqlServerFact]
    public async Task F018_S2_a_customer_without_a_credit_limit_is_never_within_and_the_shipments_exception_is_embedded_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var counts = await run.WriteCountsAsync();

        var first = await run.GateAsync(CompanyA, draft.Id, Shipment);
        AssertHeldWithoutLimit(first, draft.Id);
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.None, (int?)null), (first.Exception.Status, first.Exception.ExceptionNo));
        Assert.Equal(SalesShipmentRiskGateContractV1.Version, first.ContractVersion);
        // A read only: no order, exception, attempt or audit row moved.
        Assert.Equal(counts, await run.WriteCountsAsync());

        // A requested (pending) exception of this shipment is embedded as the exception port answers it; the risk half is unchanged.
        await run.PendingExceptionAsync(CompanyA, draft.Id, Shipment);
        var second = await run.GateAsync(CompanyA, draft.Id, Shipment);
        AssertHeldWithoutLimit(second, draft.Id);
        Assert.Equal((first.Result, first.HoldReason), (second.Result, second.HoldReason));
        Assert.Equal((SalesShipmentRiskExceptionStatusV1.Pending, (int?)1), (second.Exception.Status, second.Exception.ExceptionNo));

        // Another shipment of the same order has no exception of its own: one exception never releases two shipments.
        var otherShipment = Guid.NewGuid();
        var other = await run.GateAsync(CompanyA, draft.Id, otherShipment);
        Assert.Equal((otherShipment, SalesShipmentRiskExceptionStatusV1.None), (other.LoadingInstructionId, other.Exception.Status));
    }

    [SqlServerFact]
    public async Task F018_S2_another_company_and_an_unknown_order_answer_source_unavailable_with_no_exception_never_within_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        await run.PendingExceptionAsync(CompanyA, draft.Id, Shipment);

        var foreign = await run.GateAsync(CompanyB, draft.Id, Shipment);
        Assert.Equal((SalesShipmentRiskResultV1.Unavailable, (SalesShipmentRiskHoldReasonV1?)SalesShipmentRiskHoldReasonV1.SourceUnavailable,
            SalesShipmentRiskExceptionStatusV1.None), (foreign.Result, foreign.HoldReason, foreign.Exception.Status));
        Assert.Equal((draft.Id, Shipment), (foreign.OrderId, foreign.LoadingInstructionId));

        var unknown = await run.GateAsync(CompanyA, Guid.NewGuid(), Shipment);
        Assert.Equal((SalesShipmentRiskResultV1.Unavailable, (SalesShipmentRiskHoldReasonV1?)SalesShipmentRiskHoldReasonV1.SourceUnavailable,
            SalesShipmentRiskExceptionStatusV1.None), (unknown.Result, unknown.HoldReason, unknown.Exception.Status));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions]"));
    }

    /// <summary>The SYNTHETIC customer has no credit limit in Finance: held as CreditLimitNotSet, or SourceUnavailable — never "within".</summary>
    private static void AssertHeldWithoutLimit(SalesShipmentRiskGateReadV1 read, Guid orderId)
    {
        Assert.Equal((orderId, Shipment), (read.OrderId, read.LoadingInstructionId));
        Assert.Equal(SalesShipmentRiskResultV1.Unavailable, read.Result);
        Assert.Contains(read.HoldReason,
            new SalesShipmentRiskHoldReasonV1?[] { SalesShipmentRiskHoldReasonV1.CreditLimitNotSet, SalesShipmentRiskHoldReasonV1.SourceUnavailable });
    }

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line TL draft from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
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
        public string Correlation { get; } = $"sales-f018-s2-{Guid.NewGuid():N}";

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

        /// <summary>The host-resolved gate port's answer under the given server-resolved company.</summary>
        public async Task<SalesShipmentRiskGateReadV1> GateAsync(Guid company, Guid orderId, Guid loadingInstructionId)
        {
            await using var hostScope = Factory.Services.CreateAsyncScope();
            hostScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
            return await hostScope.ServiceProvider.GetRequiredService<ISalesShipmentRiskGateReadPortV1>().GetAsync(orderId, loadingInstructionId);
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

        /// <summary>A pending single-shipment risk exception of the order, written raw (the request flow is API-07's own acceptance).</summary>
        public Task<int> PendingExceptionAsync(Guid company, Guid orderId, Guid loadingInstructionId) => ScalarAsync<int>(
            "INSERT INTO [sales].[ShipmentRiskExceptions] ([Id],[CompanyId],[OrderId],[ExceptionNo],[LoadingInstructionId],[Reason],[State],"
            + "[RequestedBy],[RequestedAtUtc],[Version],[UpdatedAtUtc],[CorrelationId]) VALUES (NEWID(), @company, @id, 1, @shipment, N'SENTETIK', 1, "
            + "N'SENTETIK-shipper', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), N'x'); SELECT 1",
            ("@company", company), ("@id", orderId), ("@shipment", loadingInstructionId));

        /// <summary>The rows a Sales write would move: orders and their version sum, exceptions, attempts and Sales audit events.</summary>
        public async Task<(int Drafts, long Versions, int Exceptions, int Attempts, int Audits)> WriteCountsAsync() => (
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [sales].[Drafts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptions]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ShipmentRiskExceptionAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] LIKE N'Sales.%'"));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesScope SalesStore(Guid company, TimeProvider clock)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
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
