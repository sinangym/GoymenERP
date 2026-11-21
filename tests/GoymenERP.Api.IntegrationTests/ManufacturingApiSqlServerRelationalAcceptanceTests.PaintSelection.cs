using System.Net;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Paint_selection_http_requires_the_plan_write_scope_and_masks_an_unavailable_owner()
    {
        await using var run = await Run.StartAsync();
        var writer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ErpPermissions.ManufacturingPlansWrite);
        var source = new UnavailablePaintSelectionSource();
        await using var host = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IManufacturingPaintSelectionSourceV1>();
            services.AddSingleton<IManufacturingPaintSelectionSourceV1>(source);
        }));
        using var viewer = host.CreateClient();
        viewer.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyAViewer);
        viewer.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Base}/references/paint-selections")).StatusCode);
        Assert.Equal(0, source.Reads);
        using var foreign = host.CreateClient();
        foreign.DefaultRequestHeaders.Add("X-Development-User", writer);
        foreign.DefaultRequestHeaders.Add("X-Company-Id", CompanyB.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"{Base}/references/paint-selections")).StatusCode);
        Assert.Equal(0, source.Reads);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", writer);
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString());
        using var answer = await client.GetAsync($"{Base}/references/paint-selections");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
        Assert.Equal(1, source.Reads);
        using var problem = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
        Assert.Equal("service_unavailable", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("synthetic-private-owner-detail", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private sealed class UnavailablePaintSelectionSource : IManufacturingPaintSelectionSourceV1
    {
        public int Reads;
        public Task<ManufacturingPaintCatalogV1> ListAsync(Guid companyId, string? query, CancellationToken cancellationToken)
        { Reads++; throw new InvalidOperationException("synthetic-private-owner-detail"); }
        public Task<ManufacturingPaintSelectionAnswerV1> ValidateAsync(Guid companyId, PlanPaintSelectionV1 selection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
