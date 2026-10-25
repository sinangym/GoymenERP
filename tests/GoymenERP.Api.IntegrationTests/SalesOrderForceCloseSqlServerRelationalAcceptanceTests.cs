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
using LogisticsQuantity = GoymenERP.Modules.Logistics.Domain.Quantities.Quantity;
using LogisticsQuantityUnit = GoymenERP.Modules.Logistics.Domain.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SINAN_KARARLARI_2026-10-21 No 30 (force-close) as narrowed by SAT-D12, against a REAL SQL Server and the REAL host
/// composition: the force-close preview of an approved order is read through the real routes from the owners' real ports
/// (Inventory's active allocations, Logistics's loading lines and active approved dispatch-note candidates); a fully shipped
/// line whose approved dispatch note is not yet invoiced does NOT block (SAT-D12: a warning) and the order closes in ONE save
/// (header state 3 + version, the appended force-closure row with every line's closed remainder, the attempt, the audit event
/// with SourceChannel Web) while the line quantity stays as ordered; the same key replays with no second effect; the Sales line
/// port no longer answers the lines as an approved demand; an instructed-but-not-shipped loading line blocks the closure as the
/// key's recorded refusal; another company sees nothing and the cancel name does not confer the force-close name; the
/// migration refuses to roll back over a recorded closure. Orders are stored through the real Sales store from ISOLATED
/// SYNTHETIC owner snapshots (the AT-024 precedent); every grant is a fresh synthetic role. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderForceCloseSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit ForceClosedAudit = new("sales.order-draft.force-closed.v1", "ForceClose", "SENTETIK zorla kapatıldı");

    [SqlServerFact]
    public async Task A_shipped_order_with_an_uninvoiced_dispatch_note_is_force_closed_through_the_real_routes_and_records_its_remainder_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "10");
        var orderLine = Assert.Single(draft.Lines);
        // Logistics really instructs, loads and ships 6 kg and approves a dispatch note for it (not yet invoiced).
        var noteId = await run.ApprovedDispatchNoteAsync(CompanyA, draft, "k");
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose);
        using var client = run.Client(subject, CompanyA);
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close-impact";
        var closePath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close";

        // The preview: each category from its real owner; the dispatch note is present but a warning only (SAT-D12); the
        // fully shipped instruction is the shipped part, not open loading.
        var before = await run.EffectsAsync(draft.Id);
        var preview = await run.JsonAsync(client, impactPath);
        Assert.Equal(before, await run.EffectsAsync(draft.Id));
        Assert.True(preview.GetProperty("closable").GetBoolean());
        Assert.Equal(
            [
                (SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None, false),
                (SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactStatusesV1.NoSource, false),
                (SalesCancellationImpactSourcesV1.LinkedProduction, SalesCancellationImpactStatusesV1.NoSource, false),
                (SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.None, false),
                (SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes, SalesCancellationImpactStatusesV1.Present, false),
                (SalesCancellationImpactSourcesV1.UnmatchedAdvances, SalesCancellationImpactStatusesV1.NoSource, false),
            ],
            preview.GetProperty("sources").EnumerateArray().Select(source => (source.GetProperty("source").GetString(),
                source.GetProperty("status").GetString(), source.GetProperty("blocks").GetBoolean())));
        var line = Assert.Single(preview.GetProperty("lines").EnumerateArray());
        Assert.Equal(("10", "6", "6", "4"), (line.GetProperty("ordered").GetString(), line.GetProperty("instructed").GetString(),
            line.GetProperty("shipped").GetString(), line.GetProperty("closedRemainder").GetString()));
        var token = preview.GetProperty("impactToken").GetString()!;

        using (var closed = await client.SendAsync(ForceClose(closePath, "SENTETIK-close-1", "4", "  SENTETIK kalan istenmiyor  ", token)))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.False(closed.Headers.Contains("Idempotency-Replayed"));
            var summary = (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft");
            Assert.Equal(("Closed", "Approved", "5", "Rev.0"), (summary.GetProperty("state").GetString(), summary.GetProperty("approvalState").GetString(),
                summary.GetProperty("version").GetString(), summary.GetProperty("revision").GetString()));
        }

        // ONE save, as SQL Server holds it: the header (state 3, approval 3 with its facts, version 5, Rev.0), the appended row
        // with the closed remainder, the attempt and the audit event (SourceChannel Web) — the line row keeps its quantity.
        Assert.Equal((3, 3, 5L, 0, "SENTETIK-approver"), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(("SENTETIK kalan istenmiyor", 5L, token, 1), (
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ImpactToken] FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(("10", "6", "4"), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE([LinesJson], '$[0].ordered') FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([LinesJson], '$[0].shipped') FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE([LinesJson], '$[0].closedRemainder') FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.force-closed.v1' AND [EntityId] = @id "
            + "AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.force-close.v1' AND [Outcome] = 1 AND [DraftId] = @id",
            ("@id", draft.Id)));
        Assert.Equal(("10", 10m), (
            await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [Quantity] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCancellations] WHERE [DraftId] = @id", ("@id", draft.Id)));
        // Sales touched nothing of Logistics: the dispatch note is still approved, its invoicing work stays open.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [logistics].[DispatchNotes] WHERE [DispatchNoteId] = @id AND [Status] = 2", ("@id", noteId)));

        // Close and reopen: the detail reads Closed; the record carries the reason, the preview and the closed remainder.
        var detail = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}");
        Assert.Equal(("Closed", "SENTETIK-approver", "10"), (detail.GetProperty("draft").GetProperty("state").GetString(),
            detail.GetProperty("approval").GetProperty("decidedBy").GetString(), detail.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        var record = (await run.JsonAsync(client, closePath)).GetProperty("forceClosure");
        Assert.Equal(("SENTETIK kalan istenmiyor", token, 6), (record.GetProperty("reason").GetString(), record.GetProperty("impactToken").GetString(),
            record.GetProperty("sources").GetArrayLength()));
        var recordedLine = Assert.Single(record.GetProperty("lines").EnumerateArray());
        Assert.Equal((orderLine.Id.ToString("D"), "10", "6", "4"), (recordedLine.GetProperty("lineId").GetString(),
            recordedLine.GetProperty("ordered").GetString(), recordedLine.GetProperty("shipped").GetString(),
            recordedLine.GetProperty("closedRemainder").GetString()));

        // The same key and content: the stored result replayed — no second row, event or attempt.
        var afterClose = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(ForceClose(closePath, "SENTETIK-close-1", "4", "  SENTETIK kalan istenmiyor  ", token)))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(afterClose, await run.EffectsAsync(draft.Id));

        // The next module: the line is no longer an approved demand and is no longer offered to a picker.
        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Closed"), (answer.Outcome, answer.ApprovalState));
        Assert.DoesNotContain(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id);
    }

    [SqlServerFact]
    public async Task An_instructed_but_not_shipped_loading_line_blocks_the_closure_as_the_keys_recorded_refusal_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        await run.InstructedAsync(CompanyA, draft, "k");
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose);
        using var client = run.Client(subject, CompanyA);
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close-impact";
        var closePath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close";

        var preview = await run.JsonAsync(client, impactPath);
        Assert.False(preview.GetProperty("closable").GetBoolean());
        var loading = preview.GetProperty("sources").EnumerateArray()
            .Single(source => source.GetProperty("source").GetString() == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote);
        Assert.Equal((SalesCancellationImpactStatusesV1.Present, true), (loading.GetProperty("status").GetString(), loading.GetProperty("blocks").GetBoolean()));
        var token = preview.GetProperty("impactToken").GetString()!;

        using (var blocked = await client.SendAsync(ForceClose(closePath, "SENTETIK-blocked", "4", "neden", token)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.ForceCloseBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        using (var replayed = await client.SendAsync(ForceClose(closePath, "SENTETIK-blocked", "4", "neden", token)))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        using (var stale = await client.SendAsync(ForceClose(closePath, "SENTETIK-stale", "3", "neden", token)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.force-close.v1' AND [Outcome] = 2 AND [DraftId] = @id",
            ("@id", draft.Id)));
        Assert.Equal((1, 4L, 0), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures]")));
        // Sales released nothing of Logistics: the instruction line is still there and the order line still an approved demand.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingInstructionLines] WHERE [SourceOrderId] = @id", ("@id", draft.Id)));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await run.LineAsync(CompanyA, draft.Id, orderLine.Id)).Outcome);
    }

    [SqlServerFact]
    public async Task Another_company_cannot_reach_the_order_and_the_cancel_name_does_not_confer_force_close_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        await run.InstructedAsync(CompanyB, draft, "b");

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close-impact")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close")).StatusCode);
            using var refused = await clientOfB.SendAsync(ForceClose($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close", "SENTETIK-b", "4", "neden", new string('a', 64)));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        // Company A's canceller holds the draft read and the CANCEL name only: the force-close routes are 403.
        var canceller = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel);
        using (var cancelOnly = run.Client(canceller, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cancelOnly.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close-impact")).StatusCode);
            using var refused = await cancelOnly.SendAsync(ForceClose($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close", "SENTETIK-a-cancel", "4", "neden", new string('a', 64)));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            // The record is a draft read.
            var record = await run.JsonAsync(cancelOnly, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close");
            Assert.Equal(JsonValueKind.Null, record.GetProperty("forceClosure").ValueKind);
        }

        // Company A's force-closer: B's loading line is invisible under A's tenant filter.
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose);
        using var client = run.Client(subject, CompanyA);
        var preview = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close-impact");
        Assert.True(preview.GetProperty("closable").GetBoolean());
        Assert.Equal((0, "6"), (preview.GetProperty("lines")[0].GetProperty("loadingInstructionLines").GetInt32(),
            preview.GetProperty("lines")[0].GetProperty("closedRemainder").GetString()));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.force-close.v1'"));
    }

    [SqlServerFact]
    public async Task The_force_closure_migration_refuses_to_roll_back_while_any_closure_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ForceCloseDraftAsync(run.SalesEnvelope("k-close"), Hash("k-close"), SalesDraftCommands.ForceCloseV1, draft.Id,
                (d, plan, now) => d.ForceClose(4, "SENTETIK-closer", "SENTETIK neden", plan.Impact, plan.Lines, now), ForceClosedAudit,
                (_, _) => Task.FromResult(new SalesForceClosurePlan(new ForceClosureImpactSnapshot(new string('e', 64),
                    [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None, false)]),
                    [new ForceClosureLineInput(orderLine.Id, 2.5m)])));
        }

        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var closure = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftForceClosures", StringComparison.Ordinal));
        Assert.True(closure > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[closure]);
        Assert.Equal(applied[..(closure + 1)], await context.Database.GetAppliedMigrationsAsync());
        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[closure - 1]));
        Assert.Equal(51061, error.Number);
        Assert.Equal(applied[..(closure + 1)], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        // The rebuilt order carries its closure (the store's restore reads the row back through the domain).
        await using var reader = run.SalesStore(CompanyA);
        var reread = await reader.Store.FindDraftAsync(draft.Id);
        Assert.Equal((SalesDraftState.Closed, "SENTETIK neden", 5L, "3.5", "6"), (reread!.State, reread.ForceClosure!.Reason, reread.Version,
            reread.ForceClosure.Lines[0].ClosedRemainder, reread.Lines[0].QuantityText));
        // The database refuses a second closure row and a closure of another company's order (the key and the FK).
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [sales].[DraftForceClosures] ([CompanyId],[DraftId],[Revision],[ClosedBy],[ClosedAtUtc],[Reason],[DraftVersion],[ImpactToken],[ImpactJson],[LinesJson],[CorrelationId]) "
            + "VALUES (@company, @id, 0, N'x', SYSDATETIMEOFFSET(), N'x', 5, N'x', N'{}', N'[]', N'x'); SELECT 1",
            ("@company", CompanyA), ("@id", draft.Id)));
        Assert.Equal(2627, duplicate.Number);
        var foreign = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [sales].[DraftForceClosures] ([CompanyId],[DraftId],[Revision],[ClosedBy],[ClosedAtUtc],[Reason],[DraftVersion],[ImpactToken],[ImpactJson],[LinesJson],[CorrelationId]) "
            + "VALUES (@company, @id, 0, N'x', SYSDATETIMEOFFSET(), N'x', 5, N'x', N'{}', N'not json', N'x'); SELECT 1",
            ("@company", CompanyB), ("@id", draft.Id)));
        Assert.Equal(547, foreign.Number);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage ForceClose(string path, string key, string expectedVersion, string reason, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftForceCloseRequest(expectedVersion, reason, token), SalesDraftJson.Options),
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
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, force-closures, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int ForceClosures, long DraftVersion);

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
        public string Correlation { get; } = $"sales-no30-force-close-{Guid.NewGuid():N}";

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

        /// <summary>An instruction of <paramref name="company"/> for the draft's one line (6 kg), created through the real Logistics store: instructed only.</summary>
        public async Task InstructedAsync(Guid company, SalesOrderDraft draft, string keyPrefix)
        {
            var orderLine = Assert.Single(draft.Lines);
            await using var scope = LogisticsStore(company);
            await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(6m), T0);
                return instruction;
            });
        }

        /// <summary>
        /// Creates, issues, loads, ships, drafts and approves one dispatch note for the order through Logistics's real store.
        /// </summary>
        public async Task<Guid> ApprovedDispatchNoteAsync(Guid company, SalesOrderDraft draft, string keyPrefix)
        {
            var orderLine = Assert.Single(draft.Lines);
            var instructionLineId = Guid.NewGuid();
            var shipmentId = Guid.NewGuid();
            var noteId = Guid.NewGuid();
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
            stored = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"),
                Hash($"{keyPrefix}-l-ship"), LogisticsCommands.RecordShipmentV1, stored.Instruction.Id,
                (value, now) => value.MarkShipped(stored.Instruction.Version, shipmentId, instructionLineId, Kg(6m), now),
                new LogisticsMutationAudit("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi")))!.Instruction;
            stored = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-note"),
                Hash($"{keyPrefix}-l-note"), LogisticsCommands.CreateDispatchNoteV1, stored.Instruction.Id,
                (value, now) => value.CreateDispatchNoteDraft(stored.Instruction.Version, noteId, [shipmentId], Actor, now),
                new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-created.v1", "CreateDispatchNote", "SENTETIK irsaliye taslağı")))!.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-approve-note"),
                Hash($"{keyPrefix}-l-approve-note"), LogisticsCommands.ApproveDispatchNoteV1, stored.Instruction.Id,
                (value, now) => value.ApproveDispatchNote(stored.Instruction.Version, noteId, Actor, now),
                new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-approved.v1", "ApproveDispatchNote", "SENTETIK irsaliye onaylandı"));
            return noteId;
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
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures]"),
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales force-close user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"NO30-FORCE-CLOSE-{roleId:N}", "Synthetic Sales force-close role", true);
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
