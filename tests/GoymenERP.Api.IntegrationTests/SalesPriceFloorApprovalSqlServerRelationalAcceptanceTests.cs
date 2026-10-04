using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// PL04 on real SQL Server: the first price-floor approval response is stored with the attempt in both disclosure forms,
/// survives a later state mutation, replays without a second effect, and cannot be silently dropped by migration rollback.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesPriceFloorApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Currency = Guid.Parse("37d9052a-efb1-4fa6-99aa-735d8517b175");
    private static readonly Guid MoneyProfile = Guid.Parse("e7ac408f-e25f-4495-bd66-539972269472");
    private static readonly Guid UnitProfile = Guid.Parse("803d7b5a-611e-412a-898c-748ba79bd60a");
    private static readonly Guid Product = Guid.Parse("329a29cd-8754-4140-a8b2-a459768620b9");
    private static readonly Guid PriceList = Guid.Parse("0ecdbad6-f17d-4bb2-8bfc-875cf32fcad5");
    private static readonly Guid Kg = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly DateOnly Day = new(2026, 10, 4);
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    private const string Requester = "SENTETIK-floor-requester";
    private const string Approver = "SENTETIK-floor-approver";
    private const string FloorKey = "SENTETIK-floor-approval-key";
    private const string FloorHash = "1c5d63654eb5052d417527265dc85cd0bb48d0cd233fe56f843021c65d1b3e6d";

    private static readonly SalesDraftStateAudit Priced = new(
        "sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK taban altı fiyat");
    private static readonly SalesDraftStateAudit FloorApproved = new(
        "sales.order-draft.price-floor-exceptions-approved.v1", "ApprovePriceFloorExceptions", "SENTETIK taban fiyat onayı");
    private static readonly SalesDraftStateAudit Submitted = new(
        "sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");

    [SqlServerFact]
    public async Task The_first_floor_approval_result_replays_after_submission_without_a_second_effect_and_Down_refuses_51064()
    {
        await using var run = await Run.StartAsync();
        SalesOrderDraft draft;
        SalesDraftReplayResult firstResult;
        long versionAfterSubmission;

        await using (var scope = run.Store(Company))
        {
            draft = (await scope.Store.StoreDraftAsync(Envelope("create", Requester), Hash("create"),
                documentNo => Draft(documentNo))).Draft;
            var line = Assert.Single(draft.Lines);
            var reference = new PriceReferenceSnapshot(PriceList, "SENTETIK fiyat listesi", 3, Day.AddDays(-1), Day.AddDays(30),
                "TRY", Kg, "100", "95", "ExcludingVat", "0", null);
            var priced = await scope.Store.ReviseDraftCommercialAsync(Envelope("price", Requester), Hash("price"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id,
                (target, now) => target.ReviseCommercial(target.Version,
                    [new LineCommercialInput(line.Id, new CommercialDraftFields("90", Kg, null, reference))], Requester, now),
                Priced, _ => Task.CompletedTask);
            Assert.NotNull(priced);

            var approved = await scope.Store.ReviseDraftCommercialAsync(Envelope(FloorKey, Approver), FloorHash,
                SalesDraftCommands.ApprovePriceFloorExceptionsV1, draft.Id,
                (target, now) =>
                {
                    target.ApprovePriceFloorExceptions(target.Version,
                        [new LinePriceFloorExceptionApprovalInput(line.Id, "SENTETIK sözleşmeli fiyat")], Approver, now);
                    return null;
                }, FloorApproved, _ => Task.CompletedTask, default, ReplayResult);
            firstResult = Assert.IsType<SalesDraftReplayResult>(approved!.Result);

            var submitted = await scope.Store.UpdateDraftStateAsync(Envelope("submit", Requester), Hash("submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (target, now) => target.Submit(target.Version, Requester, now), Submitted);
            versionAfterSubmission = submitted!.Draft.Version;
        }

        var attemptsBeforeReplay = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id", ("@id", draft.Id));
        var auditsBeforeReplay = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.OrderDraft' AND [EntityId] = @id",
            ("@id", draft.Id.ToString("D")));

        await using (var reopened = run.Store(Company))
        {
            var replay = await reopened.Store.FindReplayAsync(Envelope(FloorKey, Approver), FloorHash, default,
                SalesDraftCommands.ApprovePriceFloorExceptionsV1);
            Assert.NotNull(replay);
            Assert.Equal(JsonSerializer.Serialize(firstResult, SalesDraftJson.Options),
                JsonSerializer.Serialize(replay.Result, SalesDraftJson.Options));
            Assert.Equal(firstResult.Visible.Draft.Version, replay.Result!.Visible.Draft.Version);
            Assert.NotEqual(versionAfterSubmission.ToString(CultureInfo.InvariantCulture), replay.Result.Visible.Draft.Version);
            Assert.Equal(versionAfterSubmission, replay.Draft!.Version);
        }

        Assert.Equal(attemptsBeforeReplay, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(auditsBeforeReplay, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.OrderDraft' AND [EntityId] = @id",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id AND [CommandName] = @command "
            + "AND [Outcome] = 1 AND [RefusalJson] IS NULL AND ISJSON([ResultJson]) = 1",
            ("@id", draft.Id), ("@command", SalesDraftCommands.ApprovePriceFloorExceptionsV1)));

        var invalidNull = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [sales].[DraftAttempts] ([CompanyId],[CommandName],[IdempotencyKeyHash],[RequestHash],[ActorId],[Outcome],[DraftId],[CorrelationId],[RecordedAtUtc]) "
            + "VALUES (@company,@command,@key,@hash,N'SENTETIK',1,@draft,N'SENTETIK-correlation',SYSUTCDATETIME())",
            ("@company", Company), ("@command", SalesDraftCommands.ApprovePriceFloorExceptionsV1),
            ("@key", Hash("invalid-null")), ("@hash", Hash("invalid-null-body")), ("@draft", draft.Id)));
        Assert.Equal(547, invalidNull.Number);

        var invalidOccupied = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO [sales].[DraftAttempts] ([CompanyId],[CommandName],[IdempotencyKeyHash],[RequestHash],[ActorId],[Outcome],[DraftId],[ResultJson],[CorrelationId],[RecordedAtUtc]) "
            + "VALUES (@company,@command,@key,@hash,N'SENTETIK',1,@draft,N'{}',N'SENTETIK-correlation',SYSUTCDATETIME())",
            ("@company", Company), ("@command", SalesDraftCommands.ReviseCommercialV1),
            ("@key", Hash("invalid-occupied")), ("@hash", Hash("invalid-occupied-body")), ("@draft", draft.Id)));
        Assert.Equal(547, invalidOccupied.Number);

        await using var context = run.Context(Company);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, "20261004160000_AddSalesPriceFloorApprovalReplayResults");
        Assert.True(migration > 0);
        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51064, refusal.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    private static SalesDraftReplayResult ReplayResult(SalesOrderDraft draft) =>
        new(Detail(draft, SalesPriceDisclosure.Visible), Detail(draft, SalesPriceDisclosure.Withheld));

    private static SalesOrderDraftDetailView Detail(SalesOrderDraft draft, SalesPriceDisclosure disclosure)
    {
        var summary = new SalesOrderDraftSummaryView(draft.Id, draft.CompanyId, draft.DocumentNo, draft.Kind.ToString(),
            draft.State.ToString(), draft.Version.ToString(CultureInfo.InvariantCulture), draft.Revision.ToString(CultureInfo.InvariantCulture),
            draft.Header.PartyId, draft.Header.CompanyAccountId, draft.Header.CurrencyId, draft.Header.BusinessDate,
            draft.CreatedAtUtc, draft.Lines.Count, draft.ApprovalState.ToString(), SalesCommercialStageV1.Order);
        var lines = draft.Lines.Select(line => new SalesOrderDraftLineDetail(line.Id, line.LineNo, line.Product.ProductId,
            line.Product.ProductVersion, line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.QuantityText,
            disclosure == SalesPriceDisclosure.Visible ? line.Commercial.UnitPrice : null,
            disclosure == SalesPriceDisclosure.Visible ? line.Commercial.PriceUnitId : null,
            line.RequestedDeliveryDate, line.Note, null,
            disclosure == SalesPriceDisclosure.Visible ? line.Commercial.DiscountPercent : null, disclosure)).ToArray();
        return new SalesOrderDraftDetailView(summary, draft.Header.RequestedDeliveryDate, draft.Header.Note, lines,
            null, null, null, null, null, null,
            new SalesDraftApprovalV1(draft.Approval.SubmittedBy, draft.Approval.SubmittedAtUtc, draft.Approval.DecidedBy,
                draft.Approval.DecidedAtUtc, draft.Approval.RejectionReason),
            new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);
    }

    private static SalesOrderDraft Draft(string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(7));
        var money = new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "1", Day,
            true, Day.AddDays(-30), null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(Company, Kg, "kg", "weight", UnitProfile, "1", true, 3, 3, true,
            Day.AddDays(-30), null);
        var binding = new UnitBindingSnapshot(Company, Kg, UnitProfile, "1");
        var product = new ProductSnapshot(Product, "1", "SENTETIK-001", "Sentetik ürün", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "10", 10m, RequestedDeliveryDate: Day.AddDays(3)), T0);
        return draft;
    }

    private static SalesDraftWriteEnvelope Envelope(string key, string actor) =>
        new(actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], $"SENTETIK-{key}", null, key, "Web");

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Scope(SalesDbContext context, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;
        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class Run(string baseConnectionString, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return new Run(baseConnectionString, factory);
        }

        public SalesDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema)).Options, companyContext);
        }

        public Scope Store(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext))
                .Options, companyContext);
            return new Scope(context, new SalesStore(context, companyContext, collector, TimeProvider.System));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
