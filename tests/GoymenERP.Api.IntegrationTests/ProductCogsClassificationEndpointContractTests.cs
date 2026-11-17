using GoymenERP.Api.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;
public sealed class ProductCogsClassificationEndpointContractTests
{
    [Theory]
    [InlineData("GetProductCogsClassificationV1",false)] [InlineData("SetProductCogsClassificationV1",true)]
    public async Task Global_classification_routes_require_resolved_company_without_company_permission_policy(string name,bool write)
    {
        await using var factory=new GoymenErpApiFactory();using var client=factory.CreateClient();
        using var health=await client.GetAsync("/health/live");health.EnsureSuccessStatusCode();
        var route=factory.Services.GetServices<EndpointDataSource>().SelectMany(x=>x.Endpoints).OfType<RouteEndpoint>()
            .Single(x=>x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName==name);
        Assert.Equal("/api/v1/product-cogs-classifications/{productId:guid}",route.RoutePattern.RawText);
        Assert.NotNull(route.Metadata.GetMetadata<RequiresCompanyContextAttribute>());
        var auth=route.Metadata.GetOrderedMetadata<IAuthorizeData>();Assert.NotEmpty(auth);
        Assert.DoesNotContain(auth,x=>x.Policy==ProductCogsClassificationContractsV1.Manage);
        var statuses=route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(x=>x.StatusCode).ToHashSet();
        Assert.Subset(statuses,new HashSet<int>{200,400,401,403,404,503});
        Assert.Equal(write,route.Metadata.Any(x=>x.GetType().Name=="IdempotentCommandMetadata"));
        if(write)Assert.Subset(statuses,new HashSet<int>{409,415});
    }
}
