using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Actual_non_kg_consumption_requires_the_fact_grant_replays_unchanged_and_never_discloses_another_company()
    {
        await using var run = await Run.StartAsync(); var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, AllWrites);
        var (order, _) = await run.OpenOrderAsync(user); var fact = Guid.NewGuid();
        var request = new RecordManufacturingNonKgInputRequestV1(fact, 1, 0, "Other", "Synthetic packaging", "Piece", "3", At, null, "Synthetic actual consumption");
        using var client = run.ClientFor(user, CompanyA); var key = Guid.NewGuid().ToString("N");
        using var accepted = await client.PostWithKeyAsync($"{Base}/orders/{order}/non-kg-inputs", request, key);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var first = (await accepted.Content.ReadFromJsonAsync<ManufacturingNonKgInputHistoryV1>())!;
        Assert.Equal("3", Assert.Single(first.Facts).Quantity); Assert.Equal(1, first.Revision);
        using var replay = await client.PostWithKeyAsync($"{Base}/orders/{order}/non-kg-inputs", request, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var repeated = (await replay.Content.ReadFromJsonAsync<ManufacturingNonKgInputHistoryV1>())!;
        Assert.Equal(first.Facts.ToArray(), repeated.Facts.ToArray()); Assert.Equal(first.Revision, repeated.Revision);
        var reader = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.ManufacturingOrdersRead);
        using (var readOnly = run.ClientFor(reader, CompanyA))
            Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.PostWithKeyAsync($"{Base}/orders/{order}/non-kg-inputs", request)).StatusCode);
        var foreign = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingOrdersRead);
        using (var other = run.ClientFor(foreign, CompanyB)) Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Base}/orders/{order}/non-kg-inputs")).StatusCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.NonKgInputFacts"));
    }
}
