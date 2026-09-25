using GoymenERP.Modules.Accounting.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AccountingJournalReversalEndpointContractTests
{
    [Theory]
    [InlineData("ProposeAccountingJournalReversalV1", "/api/v1/accounting/journal-reversal-proposals",
        AccountingJournalReversalPermissionNamesV1.Propose, 201, false)]
    [InlineData("ApproveAccountingJournalReversalV1", "/api/v1/accounting/journal-reversal-proposals/{proposalId:guid}/approval",
        AccountingJournalReversalPermissionNamesV1.Approve, 200, true)]
    public async Task Public_routes_bind_narrow_permission_and_declared_error_contract(
        string endpointName,
        string routePrefix,
        string permission,
        int successStatus,
        bool hasNotFound)
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();

        var endpoint = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);

        Assert.StartsWith(routePrefix, endpoint.RoutePattern.RawText, StringComparison.Ordinal);
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            authorization => authorization.Policy == permission);

        var statuses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToHashSet();
        Assert.Contains(successStatus, statuses);
        Assert.Contains(400, statuses);
        Assert.Contains(403, statuses);
        Assert.Contains(409, statuses);
        Assert.Contains(422, statuses);
        Assert.Contains(503, statuses);
        Assert.Equal(hasNotFound, statuses.Contains(404));
    }
}
