using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D167 (EK24) against a REAL SQL Server and the REAL host composition: the line revision of an approved order through the real
/// Sales store — an added line's row (<c>AddedInRevision</c>), a removed line passivated in place (<c>RemovedInRevision</c>,
/// <c>ReplacedByLineId</c> for a product change; the row is never deleted), the approved revision archived, the order rebuilt with
/// the removed line out of its lines (and out of the Sales line port, so no consumer ships it) through later schedule changes; the
/// write guard never brings a passive line back, <c>CK_DraftLines_LineRevision</c> holds, another company sees nothing, and the
/// migration refuses to roll back over a line revision (51142). The drafts come from ISOLATED SYNTHETIC owner snapshots (the
/// API-03 precedent). SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderLineRevisionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("6a3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c01");
    private static readonly Guid MoneyProfile = Guid.Parse("6a3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c02");
    private static readonly Guid KgProfile = Guid.Parse("6a3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c03");
    private static readonly Guid ProductA = Guid.Parse("6a3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c04");
    private static readonly Guid ProductB = Guid.Parse("6a3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c05");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit LinesRevised = new("sales.order-draft.lines-revised.v1", "ReviseLines", "SENTETIK satır");
    private static readonly SalesDraftStateAudit Rejected = new("sales.order-draft.rejected.v1", "Reject", "SENTETIK reddedildi");
    private static readonly SalesDraftStateAudit Rescheduled = new("sales.order-draft.delivery-rescheduled.v1", "RescheduleDelivery", "SENTETIK termin");

    [SqlServerFact]
    public async Task An_approved_orders_line_revision_adds_a_row_passivates_a_line_archives_rev0_and_rebuilds_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "l");
        var first = draft.Lines[0];
        var second = draft.Lines[1];
        var replacement = Line(CompanyA, ProductB, "7");
        var approved = draft.Version;

        await using (var scope = run.SalesStore(CompanyA))
        {
            var revised = await scope.Store.ReviseDraftLinesAsync(run.SalesEnvelope("l-lines"), Hash("l-lines"), SalesDraftCommands.ReviseLinesV1,
                draft.Id, (d, now) => d.ReviseLines(approved, [replacement], [new LineRemovalInput(first.Id, replacement.LineId)], "SENTETIK-reviser",
                    "SENTETIK ürün değişti", now, Day), LinesRevised, (_, _) => Task.CompletedTask);
            Assert.Equal((1, "Preparing", approved + 1), (revised!.Draft.Revision, revised.Draft.ApprovalState.ToString(), revised.Draft.Version));
        }

        Assert.Equal((3, 1, 1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [LineNo] = 1 AND [RemovedInRevision] = 1 "
                + "AND [ReplacedByLineId] = @r", ("@id", draft.Id), ("@r", replacement.LineId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [LineNo] = 3 AND [AddedInRevision] = 1 "
                + "AND [RemovedInRevision] IS NULL AND [QuantityText] = N'7'", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId] = @id AND [EventType] = N'sales.order-draft.lines-revised.v1'",
                ("@id", draft.Id.ToString("D")))));

        // A later schedule change names only the active lines; the rebuild restores both histories.
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.RescheduleDeliveryAsync(run.SalesEnvelope("l-schedule", "SENTETIK-planner"), Hash("l-schedule"),
                SalesDraftCommands.RescheduleDeliveryV1, draft.Id,
                (d, now) => d.RescheduleDelivery(approved + 1, Day.AddDays(9), [.. d.Lines.Select(line => new LineScheduleInput(line.Id, Day.AddDays(8), []))],
                    "SENTETIK-planner", now), Rescheduled, _ => Task.CompletedTask);
        }

        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.Equal([second.Id, replacement.LineId], stored.Lines.Select(line => line.Id));
            Assert.Equal((first.Id, 1, (Guid?)replacement.LineId), (stored.RemovedLines[0].Id, stored.RemovedLines[0].RemovedInRevision,
                stored.RemovedLines[0].ReplacedByLineId));
            Assert.Equal(1, stored.Lines[1].AddedInRevision);
            Assert.All(stored.Lines, line => Assert.Equal(Day.AddDays(8), line.RequestedDeliveryDate));
            Assert.Equal(2, Assert.Single(stored.ArchivedRevisions).Lines.Count);
            Assert.Equal([first.Id, second.Id], stored.DeliveryScheduleHistory[0].Lines.Select(line => line.LineId));

            // The Sales line port never offers the passive line to a consumer.
            var port = new SalesOrderLineReferencePort(scope.Store, run.Company(CompanyA));
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(draft.Id, first.Id)).Outcome);
        }

        // Another company sees nothing of the order.
        await using (var scope = run.SalesStore(CompanyB))
        {
            Assert.Null(await scope.Store.FindDraftAsync(draft.Id));
        }
    }

    [SqlServerFact]
    public async Task The_write_guard_never_brings_a_passive_line_back_the_database_holds_its_rule_and_the_rollback_refuses_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "g");
        var first = draft.Lines[0];
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ReviseDraftLinesAsync(run.SalesEnvelope("g-lines"), Hash("g-lines"), SalesDraftCommands.ReviseLinesV1, draft.Id,
                (d, now) => d.ReviseLines(draft.Version, [], [new LineRemovalInput(first.Id)], "SENTETIK-reviser", "SENTETIK", now, Day), LinesRevised,
                (_, _) => Task.CompletedTask);
        }

        await using (var context = run.SalesContext(CompanyA))
        {
            var row = await context.DraftLines.SingleAsync(line => line.DraftId == draft.Id && line.LineNo == 1);
            row.RemovedInRevision = null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        var constraint = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[DraftLines] SET [ReplacedByLineId] = [LineId] WHERE [DraftId] = @id AND [LineNo] = 1; SELECT @@ROWCOUNT", ("@id", draft.Id)));
        Assert.Equal(547, constraint.Number);

        await using (var context = run.SalesContext(CompanyA))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var index = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftLineRevisions", StringComparison.Ordinal));
            Assert.True(index > 0);
            var refused = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[index - 1]));
            Assert.Equal(51142, refused.Number);
            Assert.Equal(applied[..(index + 1)], await context.Database.GetAppliedMigrationsAsync());
        }
    }

    [SqlServerFact]
    public async Task The_route_removes_a_line_for_a_granted_actor_replays_its_key_and_refuses_another_company_and_an_ungranted_actor_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "h");
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/line-revision";
        var removal = new SalesDraftLineRevisionRequest(draft.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "SENTETIK müşteri satırı istemiyor", [], [new SalesDraftLineRemovalV1(draft.Lines[0].Id, null)]);

        // Another company's actor holding the same name: the order does not exist for it (404), nothing recorded.
        var subjectOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersQuantityWrite);
        using (var clientOfB = run.Client(subjectOfB, CompanyB))
        {
            using var refused = await clientOfB.SendAsync(Post(path, "SENTETIK-lines-b", removal));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        // Company A's reader without the quantity-write name: 403 before anything is read or recorded.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var client = run.Client(reader, CompanyA))
        {
            using var forbidden = await client.SendAsync(Post(path, "SENTETIK-lines-a0", removal));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-lines.v1'"));

        // Granted (the owners answer no allocation and no loading line): Rev.1 in preparation, line 1 passive, the key replays.
        var writer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersQuantityWrite);
        using (var client = run.Client(writer, CompanyA))
        {
            using var revised = await client.SendAsync(Post(path, "SENTETIK-lines-a", removal));
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
            var body = (await revised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(("Rev.1", "Preparing", 1), (body.GetProperty("draft").GetProperty("revision").GetString(),
                body.GetProperty("draft").GetProperty("approvalState").GetString(), body.GetProperty("lines").GetArrayLength()));
            var passive = Assert.Single(body.GetProperty("removedLines").EnumerateArray());
            Assert.Equal((draft.Lines[0].Id.ToString("D"), "Rev.1"), (passive.GetProperty("lineId").GetString(),
                passive.GetProperty("removedInRevision").GetString()));

            using var replay = await client.SendAsync(Post(path, "SENTETIK-lines-a", removal));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isReplay").GetBoolean());
        }

        Assert.Equal((1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-lines.v1'"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [RemovedInRevision] = 1", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task A_rejected_removal_brings_the_line_back_in_the_stored_order_the_line_port_and_the_approved_line_list_on_real_sql()
    {
        // SAT-D211 (EK36): Rev.1 removes line 1, is submitted and REJECTED — the rejection reverts the removal in the stored order.
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "r");
        var first = draft.Lines[0];
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ReviseDraftLinesAsync(run.SalesEnvelope("r-d211-lines"), Hash("r-d211-lines"), SalesDraftCommands.ReviseLinesV1, draft.Id,
                (d, now) => d.ReviseLines(d.Version, [], [new LineRemovalInput(first.Id)], "SENTETIK-reviser", "SENTETIK", now, Day), LinesRevised,
                (_, _) => Task.CompletedTask);
            await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("r-d211-submit"), Hash("r-d211-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(d.Version, run.Actor, now), Submitted);
            var port = new SalesOrderLineReferencePort(scope.Store, run.Company(CompanyA));
            Assert.Equal(SalesOrderLineReferenceOutcomeV1.NotFound, (await port.GetApprovedLineAsync(draft.Id, first.Id)).Outcome);
            await scope.Store.UpdateDraftStateAsync(run.SalesEnvelope("r-d211-reject", "SENTETIK-approver"), Hash("r-d211-reject"), SalesDraftCommands.RejectV1,
                draft.Id, (d, now) => d.Reject(d.Version, "SENTETIK-approver", "SENTETIK ret", now), Rejected);
        }

        Assert.Equal((0, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [RemovedInRevision] IS NOT NULL", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [LineNo] = 1 AND [ReinstatedInRevision] = 1 "
                + "AND [ReplacedByLineId] IS NULL", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId] = @id AND [EventType] = N'sales.order-draft.rejected.v1' "
                + "AND [AfterJson] LIKE N'%revertedLines%'", ("@id", draft.Id.ToString("D")))));

        // Rebuilt from the rows: the line is active (reinstated in Rev.1) and ships under Rev.0 on the direct read and the list.
        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.Equal((2, 1, 0), (stored.Lines.Count, stored.Lines[0].ReinstatedInRevision!.Value, stored.RemovedLines.Count));
            var port = new SalesOrderLineReferencePort(scope.Store, run.Company(CompanyA));
            var answer = await port.GetApprovedLineAsync(draft.Id, first.Id);
            Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "Rejected"), (answer.Outcome, answer.Line!.Revision, answer.Line.PendingApprovalState));
            var listed = (await port.ListApprovedLinesAsync(10, null)).Items.Where(item => item.OrderId == draft.Id).Select(item => item.LineId);
            Assert.Contains(first.Id, listed);
        }

        // The guard admits no other move of the reinstatement column.
        await using (var context = run.SalesContext(CompanyA))
        {
            var row = await context.DraftLines.SingleAsync(line => line.DraftId == draft.Id && line.LineNo == 1);
            row.ReinstatedInRevision = 2;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [SqlServerFact]
    public async Task SAT_D219_the_route_refuses_a_removal_of_a_line_with_an_open_linked_supply_and_asks_the_owner_for_the_server_company_on_real_sql()
    {
        // A SYNTHETIC Purchasing adapter of the Sales-declared linked-supply port, registered in the real host.
        var owner = new LinkedSupplyOwner();
        await using var run = await Run.StartAsync(services =>
            services.AddSingleton<ISalesOrderLinkedSupplyReadPortV1>(owner));
        var draft = await run.ApprovedDraftAsync(CompanyA, "s");
        var first = draft.Lines[0];
        owner.Line = first.Id;
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/line-revision";
        var writer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersQuantityWrite);
        var removal = new SalesDraftLineRevisionRequest(draft.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "SENTETIK satır kalksın", [], [new SalesDraftLineRemovalV1(first.Id, null)]);

        using var client = run.Client(writer, CompanyA);
        // An open linked purchase order on the line: 422 on removed[0].lineId, nothing recorded, the owner asked for company A.
        owner.Mode = "open";
        using (var refused = await client.SendAsync(Post(path, "SENTETIK-d219-open", removal)))
        {
            Assert.Equal((HttpStatusCode)422, refused.StatusCode);
            var error = (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0];
            Assert.Equal(("removed[0].lineId", "sales_line_revision_line_linked"),
                (error.GetProperty("field").GetString(), error.GetProperty("code").GetString()));
        }

        // An unreadable owner: 503 (fail-closed, not recorded).
        owner.Mode = "unreadable";
        using (var unavailable = await client.SendAsync(Post(path, "SENTETIK-d219-unreadable", removal)))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-lines.v1'"));
        Assert.All(owner.Queries, query => Assert.Equal((CompanyA, draft.Id), (query.CompanyId, query.OrderId)));
        Assert.True(owner.Queries.Count >= 2);

        // Closed in its owner: the removal goes through and the line is passive.
        owner.Mode = "none";
        using (var revised = await client.SendAsync(Post(path, "SENTETIK-d219-none", removal)))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND [RemovedInRevision] = 1", ("@id", draft.Id)));
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>A SYNTHETIC Purchasing owner adapter: open on the line, unreadable, or no link — as the case sets it.</summary>
    private sealed class LinkedSupplyOwner : ISalesOrderLinkedSupplyReadPortV1
    {
        public string Mode { get; set; } = "none";
        public Guid Line { get; set; }
        public List<SalesOrderLinkedSupplyQueryV1> Queries { get; } = [];

        public string SupplyKind => SalesLinkedSupplyKindsV1.Purchasing;

        public Task<SalesOrderLinkedSupplyAnswerV1> ReadOrderLinkedSupplyAsync(SalesOrderLinkedSupplyQueryV1 query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(Mode switch
            {
                "open" => new SalesOrderLinkedSupplyAnswerV1(SalesCancellationImpactStatusesV1.Present, query.CompanyId, query.OrderId, SupplyKind,
                    [new SalesOrderLinkedSupplyItemV1(Guid.NewGuid(), Guid.NewGuid(), SalesLinkedSupplyDocumentTypesV1.PurchaseOrder, "SA-1", Line,
                        "Approved", "5", KgUnit, null, null)], null),
                "unreadable" => new SalesOrderLinkedSupplyAnswerV1(SalesCancellationImpactStatusesV1.Unreadable, query.CompanyId, query.OrderId, SupplyKind, [], "off"),
                _ => new SalesOrderLinkedSupplyAnswerV1(SalesCancellationImpactStatusesV1.None, query.CompanyId, query.OrderId, SupplyKind, [], null),
            });
        }
    }

    private static HttpRequestMessage Post<T>(string path, string key, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static SalesDraftLineInput Line(Guid company, Guid product, string quantity)
    {
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        return new SalesDraftLineInput(Guid.NewGuid(), new ProductSnapshot(product, "7", product == ProductA ? "A-1" : "B-1", "Sentetik ürün", Day,
            binding, binding, binding), unit, quantity, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3));
    }

    /// <summary>A draft with two unpriced lines (A 10 kg, B 20 kg) for the normal buyer, from SYNTHETIC owner snapshots.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30,
            BuyerTax: SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        draft.AddLine(draft.Version, Line(company, ProductA, "10"), T0);
        draft.AddLine(draft.Version, Line(company, ProductB, "20"), T0);
        return draft;
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

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
        public string Correlation { get; } = $"sales-lines-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, configureServices);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored order of the company with two lines (A 10 kg, B 20 kg), submitted and approved (version 5).</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (d, now) => d.Submit(d.Version, Actor, now), Submitted);
            return (await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(d.Version, "SENTETIK-approver", now), Approved))!.Draft;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public CompanyContext Company(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return companyContext;
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales line user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"LINES-{roleId:N}", "Synthetic Sales line role", true);
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
