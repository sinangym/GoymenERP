using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-SAL-005 ("aktif tahsis") on REAL SQL with Inventory's own allocation fixtures: the Inventory-owned
/// <see cref="InventorySalesOrderAllocationReadPort"/> counts only the ACTIVE allocations of the order's lines in its own
/// company (a released one is not active, another company's are invisible) and writes nothing; through the real host, an
/// approved Sales order with an active allocation previews "Present" and its cancellation is refused (fail-closed — Sales
/// releases nothing of Inventory); once the allocation is released IN INVENTORY, the preview is clean and the order cancels.
/// The Sales draft is stored and approved through the real Sales store from SYNTHETIC owner snapshots. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Sales_order_cancellation_is_blocked_by_an_active_allocation_until_it_is_released_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var draft = await CancellationApprovedDraftAsync(run);
        var orderLine = Assert.Single(draft.Lines);
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-CANCEL-ALLOC", 100m);
        var foreignSource = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-CANCEL-ALLOC-B", 100m);
        var otherSource = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-CANCEL-ALLOC-OTHER", 100m);
        var allocationId = Guid.NewGuid();

        InventoryPhysicalAllocationResultV1 created;
        await using (var write = run.Store(CompanyA))
        {
            created = await write.Store.CreateAsync(ReadRequest(allocationId, draft.Id, orderLine.Id, source, "30"), Metadata(run));
            // Another order's allocation on the same kind of line is not this order's.
            await write.Store.CreateAsync(ReadRequest(Guid.NewGuid(), Guid.NewGuid(), orderLine.Id, otherSource, "20"), Metadata(run));
        }

        await using (var write = run.Store(CompanyB))
        {
            // Company B's allocation naming company A's order line: B's row only.
            await write.Store.CreateAsync(ReadRequest(Guid.NewGuid(), draft.Id, orderLine.Id, foreignSource, "10"), Metadata(run));
        }

        // The owner port under each company, on real rows; the read writes nothing.
        var beforeRead = await AllocationEffectCountsAsync(run);
        var answerOfA = await ReadOrderAllocationsAsync(run, CompanyA, draft.Id);
        Assert.Equal((CompanyA, draft.Id), (answerOfA.CompanyId, answerOfA.OrderId));
        Assert.Equal((orderLine.Id, 1, 30m), (Assert.Single(answerOfA.Lines).OrderLineId, answerOfA.Lines[0].ActiveCount, answerOfA.Lines[0].ActiveRemainingKg));
        var answerOfB = await ReadOrderAllocationsAsync(run, CompanyB, draft.Id);
        Assert.Equal((CompanyB, 1, 10m), (answerOfB.CompanyId, answerOfB.Lines[0].ActiveCount, answerOfB.Lines[0].ActiveRemainingKg));
        Assert.Equal(beforeRead, await AllocationEffectCountsAsync(run));

        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersCancel);
        using var client = ClientFor(run.Factory, actor, CompanyA);
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation-impact";
        var cancelPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/cancellation";

        var blockedPreview = await CancellationJsonAsync(client, impactPath);
        Assert.False(blockedPreview.GetProperty("cancellable").GetBoolean());
        Assert.Equal(SalesCancellationImpactStatusesV1.Present, blockedPreview.GetProperty("sources")[0].GetProperty("status").GetString());
        var line = blockedPreview.GetProperty("lines")[0];
        Assert.Equal((1, "30"), (line.GetProperty("activeAllocations").GetInt32(), line.GetProperty("allocatedKg").GetString()));

        using (var blocked = await client.SendAsync(CancellationPost(cancelPath, "SYN-CANCEL-1", blockedPreview.GetProperty("impactToken").GetString()!)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((SalesDraftProblemCodesV1.CancellationBlocked, SalesCancellationImpactSourcesV1.ActiveAllocations),
                (problem.GetProperty("code").GetString(), problem.GetProperty("errors")[0].GetProperty("field").GetString()));
        }

        // Sales released nothing: the allocation is still Active in Inventory, the order is still Draft at version 4.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND Status = 1", ("@id", allocationId)));
        Assert.Equal((1, 4L), (await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));

        // The allocation released in ITS owner, by its own command: the preview is clean and the order cancels.
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.ReleaseAsync(allocationId,
                new ReleasePhysicalAllocationV1(created.Allocation.Version, "SENTETIK sipariş iptali için serbest bırakıldı"), Metadata(run));
        }

        Assert.Empty((await ReadOrderAllocationsAsync(run, CompanyA, draft.Id)).Lines);
        var cleanPreview = await CancellationJsonAsync(client, impactPath);
        Assert.True(cleanPreview.GetProperty("cancellable").GetBoolean());
        using (var cancelled = await client.SendAsync(CancellationPost(cancelPath, "SYN-CANCEL-2", cleanPreview.GetProperty("impactToken").GetString()!)))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("detail").GetProperty("draft").GetProperty("state").GetString());
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)));
    }

    private static async Task<SalesOrderAllocationReadAnswerV1> ReadOrderAllocationsAsync(Run run, Guid company, Guid orderId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(company);
        await using var context = run.Context(company);
        return await new InventorySalesOrderAllocationReadPort(context, companyContext).ReadOrderAllocationsAsync(orderId);
    }

    private static async Task<JsonElement> CancellationJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static HttpRequestMessage CancellationPost(string path, string key, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftCancelRequest("4", "SENTETIK müşteri vazgeçti", token), SalesDraftJson.Options),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    /// <summary>A one-line (6 kg) Sales draft of company A from SYNTHETIC owner snapshots, submitted and approved through the real Sales store: version 4.</summary>
    private static async Task<SalesOrderDraft> CancellationApprovedDraftAsync(Run run)
    {
        var day = new DateOnly(2026, 9, 19);
        var from = new DateOnly(2026, 9, 1);
        var at = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
        var currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e01");
        var kgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e03");
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var collector = new AuditEventCollector();
        await using var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
        var store = new SalesStore(dbContext, companyContext, collector, TimeProvider.System);
        SalesDraftWriteEnvelope Envelope(string key, string actor) =>
            new(actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], run.Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");
        string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

        var draft = (await store.StoreDraftAsync(Envelope("cancel-create", "SENTETIK-preparer"), Hash("cancel-create"), documentNo =>
        {
            var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), currency, day, day.AddDays(10));
            var money = new MoneyProfileSnapshot(CompanyA, currency, SalesOrderDraft.MoneyOperation, Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e02"),
                "3", day, true, from, null, 2);
            var created = SalesOrderDraft.Create(Guid.NewGuid(), CompanyA, documentNo, header, money, at);
            var unit = new UnitProfileSnapshot(CompanyA, SettingsUnitIdentitiesV1.Kilogram, "kg", "weight", kgProfile, "4", true, 3, 3, true, from, null);
            var binding = new UnitBindingSnapshot(CompanyA, SettingsUnitIdentitiesV1.Kilogram, kgProfile, "4");
            var product = new ProductSnapshot(Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e04"), "7", "GDC-001", "Sentetik bant", day, binding, binding, binding);
            created.AddLine(created.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "6", 6m, day.AddDays(3)), at);
            return created;
        })).Draft;
        await store.UpdateDraftStateAsync(Envelope("cancel-submit", "SENTETIK-preparer"), Hash("cancel-submit"), SalesDraftCommands.SubmitV1, draft.Id,
            (d, now) => d.Submit(2, "SENTETIK-preparer", now), new SalesDraftStateAudit("sales.order-draft.submitted.v1", "Submit", "SENTETIK"));
        return (await store.UpdateDraftStateAsync(Envelope("cancel-approve", "SENTETIK-approver"), Hash("cancel-approve"), SalesDraftCommands.ApproveV1,
            draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), new SalesDraftStateAudit("sales.order-draft.approved.v1", "Approve", "SENTETIK")))!.Draft;
    }
}
