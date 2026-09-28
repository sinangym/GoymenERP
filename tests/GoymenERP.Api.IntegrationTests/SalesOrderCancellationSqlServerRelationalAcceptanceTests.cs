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
/// DEC-SAL-005 / AT-007 against a REAL SQL Server and the REAL host composition: the impact preview of an approved order is
/// read through the real routes from the owners' real ports (Inventory's active allocations, Logistics's loading lines — each
/// registered by its own module; the other four categories have no owner on main and read "NoSource"); the cancellation is one
/// save (header state + version, the appended cancellation row, the attempt, the audit event with SourceChannel Web); the
/// reopened order reads Cancelled with its reason and unchanged approval facts; the same key replays with no second effect;
/// the Sales line port no longer answers its lines as an approved demand; a loading line of the order blocks the cancellation
/// (the fail-closed reading) as the key's recorded refusal; another company sees nothing; the migration refuses to roll back
/// over a recorded cancellation. The allocation half of the block is proven with Inventory's own fixtures in
/// <c>InventorySqlServerRelationalAcceptanceTests.Sales_order_cancellation_is_blocked_by_an_active_allocation_until_it_is_released_on_real_SQL</c>.
/// The draft is stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the AT-024
/// precedent); every grant is a fresh synthetic role. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderCancellationSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7d04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit CancelledAudit = new("sales.order-draft.cancelled.v1", "Cancel", "SENTETIK iptal");

    [SqlServerFact]
    public async Task An_approved_order_is_cancelled_through_the_real_routes_after_its_real_owner_preview_and_reopens_cancelled_with_its_history_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel);
        using var client = run.Client(subject, CompanyA);
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation-impact";
        var cancelPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation";

        // Before: the line is an approved demand for the consumers (the port Logistics and Inventory ask).
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await run.LineAsync(CompanyA, draft.Id, orderLine.Id)).Outcome);

        // The preview: each category from its real owner; nothing recorded; no source on main shown as such, never zero.
        var before = await run.EffectsAsync(draft.Id);
        var preview = await run.JsonAsync(client, impactPath);
        Assert.Equal(before, await run.EffectsAsync(draft.Id));
        Assert.True(preview.GetProperty("cancellable").GetBoolean());
        Assert.Equal(
            [
                (SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None),
                (SalesCancellationImpactSourcesV1.LinkedPurchasing, SalesCancellationImpactStatusesV1.NoSource),
                (SalesCancellationImpactSourcesV1.LinkedProduction, SalesCancellationImpactStatusesV1.NoSource),
                (SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, SalesCancellationImpactStatusesV1.None),
                (SalesCancellationImpactSourcesV1.UninvoicedDispatchNotes, SalesCancellationImpactStatusesV1.NoSource),
                (SalesCancellationImpactSourcesV1.UnmatchedAdvances, SalesCancellationImpactStatusesV1.NoSource),
            ],
            preview.GetProperty("sources").EnumerateArray().Select(source => (source.GetProperty("source").GetString(), source.GetProperty("status").GetString())));
        var line = Assert.Single(preview.GetProperty("lines").EnumerateArray());
        Assert.Equal((0, "0", 0, "0"), (line.GetProperty("activeAllocations").GetInt32(), line.GetProperty("allocatedKg").GetString(),
            line.GetProperty("loadingInstructionLines").GetInt32(), line.GetProperty("instructed").GetString()));
        var token = preview.GetProperty("impactToken").GetString()!;

        // The cancellation through the real route, with a reason and the preview's token.
        using (var cancelled = await client.SendAsync(Cancel(cancelPath, "SENTETIK-cancel-1", "4", "  SENTETIK müşteri vazgeçti  ", token)))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.False(cancelled.Headers.Contains("Idempotency-Replayed"));
            var summary = (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft");
            Assert.Equal(("Cancelled", "Approved", "5", "Rev.0"), (summary.GetProperty("state").GetString(), summary.GetProperty("approvalState").GetString(),
                summary.GetProperty("version").GetString(), summary.GetProperty("revision").GetString()));
        }

        // ONE save, as SQL Server holds it: the header (state 2, approval 3 with its facts, version 5, Rev.0), the appended row,
        // the attempt and the audit event (SourceChannel Web) — no line row changed.
        Assert.Equal((2, 3, 5L, 0, "SENTETIK-approver"), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(("SENTETIK müşteri vazgeçti", 5L, token, 1), (
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftCancellations] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftCancellations] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ImpactToken] FROM [sales].[DraftCancellations] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCancellations] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.cancelled.v1' AND [EntityId] = @id "
            + "AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.cancel.v1' AND [Outcome] = 1 AND [DraftId] = @id",
            ("@id", draft.Id)));
        Assert.Equal("6", await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)));

        // Close and reopen: the detail reads Cancelled, the record carries the reason and the preview it was decided on.
        var detail = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}");
        Assert.Equal(("Cancelled", "SENTETIK-approver"), (detail.GetProperty("draft").GetProperty("state").GetString(),
            detail.GetProperty("approval").GetProperty("decidedBy").GetString()));
        var record = (await run.JsonAsync(client, cancelPath)).GetProperty("cancellation");
        Assert.Equal(("SENTETIK müşteri vazgeçti", token, 6), (record.GetProperty("reason").GetString(), record.GetProperty("impactToken").GetString(),
            record.GetProperty("sources").GetArrayLength()));

        // The same key and content: the stored result replayed — no second row, event or attempt.
        var afterCancel = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Cancel(cancelPath, "SENTETIK-cancel-1", "4", "  SENTETIK müşteri vazgeçti  ", token)))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(afterCancel, await run.EffectsAsync(draft.Id));

        // The next module: the line is no longer an approved demand and is no longer offered to a picker.
        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "Cancelled"), (answer.Outcome, answer.ApprovalState));
        Assert.DoesNotContain(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id);
    }

    [SqlServerFact]
    public async Task A_loading_line_of_the_order_blocks_the_cancellation_as_the_keys_recorded_refusal_and_a_stale_version_is_terminal_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        await run.InstructedAsync(CompanyA, draft, "k");
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel);
        using var client = run.Client(subject, CompanyA);
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation-impact";
        var cancelPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation";

        // The instruction is only instructed (nothing loaded or shipped): it is still a record Logistics holds for the order.
        var preview = await run.JsonAsync(client, impactPath);
        Assert.False(preview.GetProperty("cancellable").GetBoolean());
        Assert.Equal(SalesCancellationImpactStatusesV1.Present, preview.GetProperty("sources").EnumerateArray()
            .Single(source => source.GetProperty("source").GetString() == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote)
            .GetProperty("status").GetString());
        var line = Assert.Single(preview.GetProperty("lines").EnumerateArray());
        Assert.Equal((orderLine.Id.ToString("D"), 1, "6", "0"), (line.GetProperty("lineId").GetString(), line.GetProperty("loadingInstructionLines").GetInt32(),
            line.GetProperty("instructed").GetString(), line.GetProperty("shipped").GetString()));
        var token = preview.GetProperty("impactToken").GetString()!;

        using (var blocked = await client.SendAsync(Cancel(cancelPath, "SENTETIK-blocked", "4", "neden", token)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.CancellationBlocked, problem.GetProperty("code").GetString());
            Assert.Equal(SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote, problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        using (var replayed = await client.SendAsync(Cancel(cancelPath, "SENTETIK-blocked", "4", "neden", token)))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        // A stale version is the key's terminal 409 too; neither refusal moved the order.
        using (var stale = await client.SendAsync(Cancel(cancelPath, "SENTETIK-stale", "3", "neden", token)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.cancel.v1' AND [Outcome] = 2 AND [DraftId] = @id",
            ("@id", draft.Id)));
        Assert.Equal((1, 4L, 0), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCancellations]")));
        // Sales released nothing of Logistics: the instruction line is still there.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingInstructionLines] WHERE [SourceOrderId] = @id", ("@id", draft.Id)));
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await run.LineAsync(CompanyA, draft.Id, orderLine.Id)).Outcome);
    }

    [SqlServerFact]
    public async Task Another_company_can_neither_preview_nor_cancel_nor_read_the_order_and_its_owners_records_never_reach_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        // Company B records a loading line naming company A's order line (a foreign binding): B's rows only.
        await run.InstructedAsync(CompanyB, draft, "b");

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation-impact")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation")).StatusCode);
            using var refused = await clientOfB.SendAsync(Cancel($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation", "SENTETIK-b", "4", "neden", new string('a', 64)));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        // Company A's viewer holds no Sales name: 403 before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation-impact")).StatusCode);
        }

        // Company A's preview: B's loading line is invisible under A's tenant filter.
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel);
        using var client = run.Client(subject, CompanyA);
        var preview = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation-impact");
        Assert.True(preview.GetProperty("cancellable").GetBoolean());
        Assert.Equal(0, preview.GetProperty("lines")[0].GetProperty("loadingInstructionLines").GetInt32());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.cancel.v1'"));
    }

    [SqlServerFact]
    public async Task The_cancellation_migration_refuses_to_roll_back_while_any_cancellation_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.CancelDraftAsync(run.SalesEnvelope("k-cancel"), Hash("k-cancel"), SalesDraftCommands.CancelV1, draft.Id,
                (d, impact, now) => d.Cancel(4, "SENTETIK-canceller", "SENTETIK neden", impact, now), CancelledAudit,
                (_, _) => Task.FromResult(new CancellationImpactSnapshot(new string('e', 64),
                    [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None)])));
        }

        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_AddRevisionReasons", applied[^1], StringComparison.Ordinal);
        Assert.EndsWith("_AddDraftCancellations", applied[^2], StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[^3]));
        Assert.Equal(51055, error.Number);
        Assert.Equal(applied[..^1], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCancellations] WHERE [DraftId] = @id", ("@id", draft.Id)));
        // The empty AddRevisionReasons above it did roll back (no reason stored), so the schema is one migration behind
        // the model; re-apply it before reading the order back through the current model.
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        // The rebuilt order carries its cancellation (the store's restore reads the row back through the domain).
        await using var reader = run.SalesStore(CompanyA);
        var reread = await reader.Store.FindDraftAsync(draft.Id);
        Assert.Equal((SalesDraftState.Cancelled, "SENTETIK neden", 5L), (reread!.State, reread.Cancellation!.Reason, reread.Version));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage Cancel(string path, string key, string expectedVersion, string reason, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftCancelRequest(expectedVersion, reason, token), SalesDraftJson.Options),
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

    /// <summary>(sales attempts, audit events, cancellations, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Cancellations, long DraftVersion);

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
        public string Correlation { get; } = $"sales-dec-sal-005-{Guid.NewGuid():N}";

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

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftCancellations]"),
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales cancellation user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"DEC-SAL-005-{roleId:N}", "Synthetic Sales cancellation role", true);
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
