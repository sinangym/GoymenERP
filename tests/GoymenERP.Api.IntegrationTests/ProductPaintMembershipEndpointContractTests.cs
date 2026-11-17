using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Public source contract. It does not grant the new permission or activate the Product runtime.</summary>
public sealed class ProductPaintMembershipEndpointContractTests
{
    [Theory]
    [InlineData("GetProductPaintMembershipsV1", "/current", false)]
    [InlineData("ValidateProductPaintMembershipImportV1", "/imports/validate", false)]
    [InlineData("ApplyProductPaintMembershipImportV1", "/imports", true)]
    [InlineData("SetProductPaintMembershipV1", "/products/{productId:guid}", true)]
    public async Task Paint_routes_require_the_explicit_company_permission_and_declare_their_real_error_contract(string name, string suffix, bool command)
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var health = await client.GetAsync("/health/live");
        health.EnsureSuccessStatusCode();
        var route = factory.Services.GetServices<EndpointDataSource>().SelectMany(x => x.Endpoints).OfType<RouteEndpoint>()
            .Single(x => x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
        Assert.Equal("/api/v1/product-paint-memberships" + suffix, route.RoutePattern.RawText);
        Assert.NotNull(route.Metadata.GetMetadata<RequiresCompanyContextAttribute>());
        Assert.Contains(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), x => x.Policy == ProductPaintMembershipPermissionsV1.Manage);
        var statuses = route.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(x => x.StatusCode).ToHashSet();
        Assert.Subset(statuses, new HashSet<int> { 200, 400, 401, 403, 503 });
        Assert.Equal(command, route.Metadata.Any(x => x.GetType().Name == "IdempotentCommandMetadata"));
        if (command) Assert.Subset(statuses, new HashSet<int> { 404, 409, 415, 422 });
    }
    [Fact]
    public void Paint_membership_capability_uses_its_narrow_company_permission_without_global_master_authority()
    {
        var provider = new ProductPaintMembershipShellCapabilityProvider();
        var declaration = Assert.Single(provider.GetDeclarations());
        Assert.Equal(("paint-memberships.manage", "/boya-urunleri", "STK-S13"), (declaration.CapabilityId, declaration.RoutePath, declaration.ScreenId));
        Assert.Equal(new[] { ProductPaintMembershipPermissionsV1.Manage }, declaration.RequiredPermissions);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, declaration.ScopeRequirement);
        Assert.Null(declaration.GlobalProductOperation);
        Assert.True(declaration.ImplementationAvailable);
    }
}
