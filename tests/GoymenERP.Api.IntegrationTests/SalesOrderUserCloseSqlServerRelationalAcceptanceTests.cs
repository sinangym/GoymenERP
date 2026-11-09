using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D32 / SAT-D168 / SAT-D215 against a REAL SQL Server and the REAL host composition: the user closure of a "kapanmaya hazır"
/// order. On main's composition the source, invoicing and financial-closure axes are NoSource, so the command is the key's
/// recorded 409 (replayed, nothing written); a holder of the force-close name only is 403; another company sees 404. With the
/// TEST SEAM (a synthetic owner of the three owner-less axes and the SAT-D5 ladder as synthetic Settings data) and Logistics
/// really shipping the line in full, the status read says ready and the route closes the order in ONE save (header state 3 +
/// version, the appended user-closure row, the attempt, the audit event with SourceChannel Web); the same key replays; the line
/// port answers the lines as not approved ("Closed"). The store, the EF write guard and the database constraints keep the row
/// append-only and never beside a force-closure; the migration refuses to roll back over a closure (51212). Orders are stored
/// through the real Sales store from ISOLATED SYNTHETIC owner snapshots; every grant is a fresh synthetic role. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderUserCloseSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f04");
    private static readonly Guid TolerancePolicyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f05");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit ClosedAudit = new("sales.order-draft.closed.v1", "Close", "SENTETIK kapatıldı");

    [SqlServerFact]
    public async Task A_ready_order_is_closed_through_the_real_route_in_one_save_replays_and_its_lines_stop_being_a_demand_on_real_sql()
    {
        await using var run = await Run.StartAsync(readySeam: true);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        await run.ShippedInFullAsync(CompanyA, draft, "k");
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using var client = run.Client(subject, CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close";

        // The status read (the same calculation the command recomputes) says ready.
        var status = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status");
        Assert.Equal(("Completed", true), (status.GetProperty("realization").GetProperty("state").GetString(),
            status.GetProperty("readyToClose").GetBoolean()));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await run.LineAsync(CompanyA, draft.Id, orderLine.Id)).Outcome);

        using (var closed = await client.SendAsync(Close(path, "SENTETIK-close-1", "4")))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.False(closed.Headers.Contains("Idempotency-Replayed"));
            var summary = (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft");
            Assert.Equal(("Closed", "Approved", "5"), (summary.GetProperty("state").GetString(), summary.GetProperty("approvalState").GetString(),
                summary.GetProperty("version").GetString()));
        }

        // ONE save, as SQL Server holds it: header state 3 / version 5, the appended row, the attempt and the audit event.
        Assert.Equal((3, 3, 5L), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal((1, 5L, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftUserClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftUserClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.closed.v1' AND [EntityId] = @id "
            + "AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5 AND [PermissionCode] = N'sales.orders.close'",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.close.v1' AND [Outcome] = 1 AND [DraftId] = @id",
            ("@id", draft.Id)));
        Assert.Equal("6", await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)));

        // The record: a user closure (the force-closure record stays empty).
        var record = (await run.JsonAsync(client, path)).GetProperty("userClosure");
        Assert.Equal("Rev.0", record.GetProperty("revision").GetString());
        Assert.Equal(JsonValueKind.Null, (await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close"))
            .GetProperty("forceClosure").ValueKind);
        Assert.False((await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status")).GetProperty("readyToClose").GetBoolean());

        // The same key and content: the stored result replayed — no second row, event or attempt.
        var afterClose = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Close(path, "SENTETIK-close-1", "4")))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(afterClose, await run.EffectsAsync(draft.Id));
        // The same key with another body is one 409, nothing written.
        using (var mismatch = await client.SendAsync(Close(path, "SENTETIK-close-1", "5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.SameKeyPayloadMismatch, (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(afterClose, await run.EffectsAsync(draft.Id));

        // The next module: the closed order's line is no longer an approved demand, exactly as after a force-closure.
        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Closed"), (answer.Outcome, answer.ApprovalState));
        Assert.DoesNotContain(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id);
    }

    [SqlServerFact]
    public async Task On_mains_composition_the_close_is_the_keys_recorded_not_ready_409_403_without_the_name_and_404_for_another_company_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        await run.ShippedInFullAsync(CompanyA, draft, "k");
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close";

        // Another company: 404 for the command and the record; nothing recorded.
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            using var refused = await clientOfB.SendAsync(Close(path, "SENTETIK-b", "4"));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync(path)).StatusCode);
        }

        // The force-close and cancel names do not confer the close name: 403, nothing recorded.
        var forceCloser = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersForceClose, SalesPermissionManifestV1.OrdersCancel);
        using (var forceOnly = run.Client(forceCloser, CompanyA))
        {
            using var refused = await forceOnly.SendAsync(Close(path, "SENTETIK-force-only", "4"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await run.JsonAsync(forceOnly, path)).GetProperty("userClosure").ValueKind);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.close.v1'"));

        // Granted, but on main the owner-less axes are NoSource: never ready — the key's recorded 409, replayed.
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using var client = run.Client(subject, CompanyA);
        var status = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status");
        Assert.Equal(("NoSource", false), (status.GetProperty("source").GetProperty("state").GetString(), status.GetProperty("readyToClose").GetBoolean()));
        using (var notReady = await client.SendAsync(Close(path, "SENTETIK-not-ready", "4")))
        {
            Assert.Equal(HttpStatusCode.Conflict, notReady.StatusCode);
            var problem = await notReady.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.OrderNotReadyToClose, problem.GetProperty("code").GetString());
            Assert.Equal("readyToClose", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        var afterRefusal = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Close(path, "SENTETIK-not-ready", "4")))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(afterRefusal, await run.EffectsAsync(draft.Id));
        Assert.Equal((1, 1, 4L, 0), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.close.v1' AND [Outcome] = 2"),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftUserClosures]")));
    }

    [SqlServerFact]
    public async Task The_store_appends_one_user_closure_the_write_guard_and_constraints_keep_it_and_the_restore_refuses_two_closures_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        await using (var scope = run.SalesStore(CompanyA))
        {
            var result = await scope.Store.CloseDraftAsync(run.SalesEnvelope("k-close"), Hash("k-close"), SalesDraftCommands.CloseV1, draft.Id,
                (d, now) => d.CloseByUser(4, "SENTETIK-closer", now), ClosedAudit, (_, _) => Task.CompletedTask);
            Assert.Equal((SalesDraftState.Closed, 5L), (result!.Draft.State, result.Draft.Version));
            // Another command name is refused before anything is read.
            await Assert.ThrowsAsync<ArgumentException>(() => scope.Store.CloseDraftAsync(run.SalesEnvelope("k-close-x"), Hash("k-close-x"),
                SalesDraftCommands.ForceCloseV1, draft.Id, (d, now) => d.CloseByUser(5, "x", now), ClosedAudit, (_, _) => Task.CompletedTask));
        }

        await using (var reader = run.SalesStore(CompanyA))
        {
            var reread = await reader.Store.FindDraftAsync(draft.Id);
            Assert.Equal((SalesDraftState.Closed, "SENTETIK-closer", 5L, (ForceClosureFacts?)null), (reread!.State, reread.UserClosure!.ClosedBy,
                reread.Version, reread.ForceClosure));
        }

        // The EF write guard: a recorded user closure is append-only.
        await using (var context = run.SalesContext(CompanyA))
        {
            var row = await context.DraftUserClosures.SingleAsync(candidate => candidate.DraftId == draft.Id);
            row.ClosedBy = "SENTETIK-other";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = run.SalesContext(CompanyA))
        {
            context.DraftUserClosures.Remove(await context.DraftUserClosures.SingleAsync(candidate => candidate.DraftId == draft.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        // The database: a second row (PK), another company's row for this draft (FK) and a version below the smallest closable one (CK).
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [sales].[DraftUserClosures] ([CompanyId],[DraftId],[Revision],[ClosedBy],[ClosedAtUtc],[DraftVersion],[CorrelationId]) "
            + "VALUES (@company, @id, 0, N'x', SYSDATETIMEOFFSET(), 5, N'x'); SELECT 1", ("@company", CompanyA), ("@id", draft.Id)));
        Assert.Equal(2627, duplicate.Number);
        var foreign = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [sales].[DraftUserClosures] ([CompanyId],[DraftId],[Revision],[ClosedBy],[ClosedAtUtc],[DraftVersion],[CorrelationId]) "
            + "VALUES (@company, @id, 0, N'x', SYSDATETIMEOFFSET(), 5, N'x'); SELECT 1", ("@company", CompanyB), ("@id", draft.Id)));
        Assert.Equal(547, foreign.Number);
        var other = await run.ApprovedDraftAsync(CompanyA, "o", "6");
        var tooLow = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [sales].[DraftUserClosures] ([CompanyId],[DraftId],[Revision],[ClosedBy],[ClosedAtUtc],[DraftVersion],[CorrelationId]) "
            + "VALUES (@company, @id, 0, N'x', SYSDATETIMEOFFSET(), 4, N'x'); SELECT 1", ("@company", CompanyA), ("@id", other.Id)));
        Assert.Equal(547, tooLow.Number);

        // A closed header beside BOTH a user closure and a force-closure is refused on read, not repaired.
        await run.ScalarAsync<int>(
            "INSERT INTO [sales].[DraftForceClosures] ([CompanyId],[DraftId],[Revision],[ClosedBy],[ClosedAtUtc],[Reason],[DraftVersion],[ImpactToken],[ImpactJson],[LinesJson],[CorrelationId]) "
            + "VALUES (@company, @id, 0, N'x', SYSDATETIMEOFFSET(), N'x', 5, N'x', N'{\"token\":\"x\",\"sources\":[]}', N'[]', N'x'); SELECT 1",
            ("@company", CompanyA), ("@id", draft.Id));
        await using (var reader = run.SalesStore(CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.Store.FindDraftAsync(draft.Id));
        }
    }

    [SqlServerFact]
    public async Task The_user_closure_migration_refuses_to_roll_back_while_any_user_closure_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.CloseDraftAsync(run.SalesEnvelope("k-close"), Hash("k-close"), SalesDraftCommands.CloseV1, draft.Id,
                (d, now) => d.CloseByUser(4, "SENTETIK-closer", now), ClosedAudit, (_, _) => Task.CompletedTask);
        }

        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var closure = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftUserClosures", StringComparison.Ordinal));
        Assert.True(closure > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[closure]);
        Assert.Equal(applied[..(closure + 1)], await context.Database.GetAppliedMigrationsAsync());
        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[closure - 1]));
        Assert.Equal(51212, error.Number);
        Assert.Equal(applied[..(closure + 1)], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftUserClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        await using var reader = run.SalesStore(CompanyA);
        Assert.Equal(SalesDraftState.Closed, (await reader.Store.FindDraftAsync(draft.Id))!.State);
    }

    [SqlServerFact]
    public async Task Without_any_user_closure_the_migration_rolls_back_and_forward_freely()
    {
        await using var run = await Run.StartAsync();
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var closure = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftUserClosures", StringComparison.Ordinal));
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[closure - 1]);
        Assert.Equal(applied[..closure], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'DraftUserClosures'"));
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage Close(string path, string key, string expectedVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftCloseRequest(expectedVersion), SalesDraftJson.Options),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity, no slices) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, user closures, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int UserClosures, long DraftVersion);

    /// <summary>TEST SEAM: synthetic owners that call the source, invoicing and financial-closure axes complete for the asking company.</summary>
    private sealed class SyntheticCompleteOwnerAxes(ICompanyContext company) : ISalesOrderOwnerAxesSourceV1
    {
        public Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SalesOrderOwnerAxesV1?>(new SalesOrderOwnerAxesV1(company.CompanyId, orderId,
                new("SENTETIK-FullySourced", true), new("SENTETIK-FullyInvoiced", true), new("SENTETIK-FinanciallyClosed", true)));
    }

    /// <summary>TEST SEAM: the SAT-D5 starting ladder (1 / 2 / 3 %) as synthetic Settings data for the asking company.</summary>
    private sealed class SyntheticToleranceLadder(ICompanyContext company) : ISettingsSalesPolicyProviderV1
    {
        public Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesDeliveryTolerancePolicyV1(company.CompanyId, new ProfileVersionV1(TolerancePolicyProfile, 3), 1m, 2m, 3m, true,
                From, null));

        public Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(Guid limitGroupId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("SENTETIK: no discount limit in this suite.");

        public Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(Guid currencyId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("SENTETIK: no approval threshold in this suite.");
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

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
        public string Correlation { get; } = $"sales-sat-d32-close-{Guid.NewGuid():N}";

        /// <summary>
        /// The real host. <paramref name="readySeam"/>: the TEST SEAM of SAT-D32 — a synthetic owner of the three owner-less axes
        /// that calls them complete and the SAT-D5 starting ladder as synthetic Settings data (nothing on main answers either),
        /// so the status read and the command can reach "kapanmaya hazır". Without it the host is main's composition.
        /// </summary>
        public static async Task<Run> StartAsync(bool readySeam = false)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, readySeam
                ? services =>
                {
                    services.RemoveAll<ISalesOrderOwnerAxesSourceV1>();
                    services.AddScoped<ISalesOrderOwnerAxesSourceV1, SyntheticCompleteOwnerAxes>();
                    services.RemoveAll<ISettingsSalesPolicyProviderV1>();
                    services.AddScoped<ISettingsSalesPolicyProviderV1, SyntheticToleranceLadder>();
                }
                : null);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>Logistics really instructs, loads and ships the draft's one line in full (6 kg) through its real store.</summary>
        public async Task ShippedInFullAsync(Guid company, SalesOrderDraft draft, string keyPrefix)
        {
            var orderLine = Assert.Single(draft.Lines);
            var instructionLineId = Guid.NewGuid();
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"),
                Hash($"{keyPrefix}-l-create"), Day, () =>
                {
                    var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId,
                        draft.Header.CompanyAccountId, T0);
                    instruction.AddLine(instruction.Version, instructionLineId,
                        new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision), orderLine.Product.ProductId,
                        KgUnit, null, Kg(6m), T0);
                    return instruction;
                })).Instruction;
            stored = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"),
                Hash($"{keyPrefix}-l-issue"), LogisticsCommands.IssueV1, stored.Instruction.Id,
                (value, now) => value.Issue(stored.Instruction.Version, now),
                new LogisticsMutationAudit("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı")))!.Instruction;
            stored = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"),
                Hash($"{keyPrefix}-l-load"), LogisticsCommands.RecordLoadV1, stored.Instruction.Id,
                (value, now) => value.RecordLoad(stored.Instruction.Version, Guid.NewGuid(), instructionLineId, Kg(6m),
                    MeasurementKind.Measured, now),
                new LogisticsMutationAudit("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi")))!.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"),
                Hash($"{keyPrefix}-l-ship"), LogisticsCommands.RecordShipmentV1, stored.Instruction.Id,
                (value, now) => value.MarkShipped(stored.Instruction.Version, Guid.NewGuid(), instructionLineId, Kg(6m), now),
                new LogisticsMutationAudit("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi"));
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftUserClosures]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        /// <summary>The Sales line port (what Inventory and Logistics ask) over the real store of the company.</summary>
        public async Task<SalesOrderLineReferenceAnswerV1> LineAsync(Guid company, Guid orderId, Guid lineId)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return await new SalesOrderLineReferencePort(scope.Store, context).GetApprovedLineAsync(orderId, lineId);
        }

        /// <summary>The approved lines a consumer's picker is offered (the Logistics LOG-4b list), over the real store.</summary>
        public async Task<IReadOnlyList<SalesApprovedLineReferenceV1>> ApprovedLinesAsync(Guid company)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return (await new SalesOrderLineReferencePort(scope.Store, context).ListApprovedLinesAsync(100, null)).Items;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

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

        public LogisticsScope LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new LogisticsScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales close user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SATD32-CLOSE-{roleId:N}", "Synthetic Sales close role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
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
