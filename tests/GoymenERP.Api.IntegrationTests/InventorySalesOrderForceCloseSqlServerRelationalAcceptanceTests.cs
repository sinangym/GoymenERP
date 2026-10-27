using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D12 on REAL SQL with Inventory's own allocation fixtures: an ACTIVE allocation still blocks the force-close of an
/// approved Sales order (only an uninvoiced dispatch note is a warning). The preview reports the allocation source as
/// Present and blocking, the command is the key's recorded 409 <c>sales_force_close_blocked</c> naming that source, and
/// nothing is written: no closure row, the order stays Draft at version 4 and Inventory's allocation stays Active (Sales
/// releases nothing of Inventory). Once the allocation is released IN INVENTORY, the preview is clean and the order
/// force-closes. The Sales draft is stored and approved through the real Sales store from SYNTHETIC owner snapshots.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Sales_order_force_close_is_blocked_by_an_active_allocation_until_it_is_released_on_real_SQL()
    {
        await using var run = await Run.StartAsync();
        var draft = await CancellationApprovedDraftAsync(run);
        var orderLine = Assert.Single(draft.Lines);
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-FORCE-ALLOC", 100m);
        var allocationId = Guid.NewGuid();

        InventoryPhysicalAllocationResultV1 created;
        await using (var write = run.Store(CompanyA))
        {
            created = await write.Store.CreateAsync(ReadRequest(allocationId, draft.Id, orderLine.Id, source, "3"), Metadata(run));
        }

        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersForceClose);
        using var client = ClientFor(run.Factory, actor, CompanyA);
        var impactPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close-impact";
        var closePath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/force-close";

        var blockedPreview = await CancellationJsonAsync(client, impactPath);
        Assert.False(blockedPreview.GetProperty("closable").GetBoolean());
        var allocations = blockedPreview.GetProperty("sources").EnumerateArray()
            .Single(item => item.GetProperty("source").GetString() == SalesCancellationImpactSourcesV1.ActiveAllocations);
        Assert.Equal((SalesCancellationImpactStatusesV1.Present, true),
            (allocations.GetProperty("status").GetString(), allocations.GetProperty("blocks").GetBoolean()));

        using (var blocked = await client.SendAsync(ForceClosePost(closePath, "SYN-FORCE-1", blockedPreview.GetProperty("impactToken").GetString()!)))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((SalesDraftProblemCodesV1.ForceCloseBlocked, SalesCancellationImpactSourcesV1.ActiveAllocations),
                (problem.GetProperty("code").GetString(), problem.GetProperty("errors")[0].GetProperty("field").GetString()));
        }

        // Nothing written: no closure row, the order still Draft at version 4, Inventory's allocation still Active.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal((1, 4L), (await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND Status = 1", ("@id", allocationId)));

        // The allocation released in ITS owner, by its own command: the preview is clean and the order force-closes.
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.ReleaseAsync(allocationId,
                new ReleasePhysicalAllocationV1(created.Allocation.Version, "SENTETIK zorla kapatma için serbest bırakıldı"), Metadata(run));
        }

        var cleanPreview = await CancellationJsonAsync(client, impactPath);
        Assert.True(cleanPreview.GetProperty("closable").GetBoolean());
        using (var closed = await client.SendAsync(ForceClosePost(closePath, "SYN-FORCE-2", cleanPreview.GetProperty("impactToken").GetString()!)))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.Equal("Closed", (await closed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("detail").GetProperty("draft").GetProperty("state").GetString());
        }

        Assert.Equal((3, 1), (await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftForceClosures] WHERE [DraftId] = @id", ("@id", draft.Id))));
    }

    private static HttpRequestMessage ForceClosePost(string path, string key, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftForceCloseRequest("4", "SENTETIK kalan istenmiyor", token), SalesDraftJson.Options),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
