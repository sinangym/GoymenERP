using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Every named authorization policy an endpoint demands must exist — which, here, means the name is
/// registered in <see cref="ErpPermissions"/>.
///
/// WHY THIS IS A 500 AND NOT A REFUSAL. Policies are not declared one by one: they are built by
/// enumerating the registry (<c>ErpAuthenticationExtensions</c>, <c>foreach (var permission in
/// ErpPermissions.All) options.AddPolicy(...)</c>). So a route that demands a name the registry does
/// not know demands a policy that was never built, and ASP.NET Core throws while authorizing the
/// request — an unhandled 500, not a 403 and not a 503. The failure is per request and invisible until
/// someone calls that route with the right shape.
///
/// WHY IT IS READ FROM THE RUNNING APPLICATION RATHER THAN FROM THE SOURCE. The obvious guard is to
/// search the source for <c>RequireAuthorization("…")</c>. That guard is worthless, and the reason is
/// recorded because it already cost a false claim in #373's body: every call site passes a CONSTANT
/// (<c>ErpPermissions.X</c>, <c>AccountingPermissionManifestV1.Y</c> — 35 call sites, 25 distinct names
/// at the time of writing), so a literal search finds nothing and concludes, wrongly, that no route
/// uses a named policy. A literal search searches a SPELLING; this rule is about what a name IS. So the
/// endpoints are read after the host has built them, where the names are values rather than syntax.
///
/// The finding behind this guard is ORG's (#373 review, P2-1).
/// </summary>
public sealed class EndpointAuthorizationPolicyRegistrationTests
{
    [Fact]
    public async Task Every_named_policy_an_endpoint_demands_is_a_registered_permission()
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var demanded = DemandedPolicyNames(factory.Services);
        // A rule that filtered nothing would pass vacuously: the application really does name policies.
        Assert.NotEmpty(demanded);

        var unregistered = demanded.Where(name => !ErpPermissions.IsKnown(name)).Order(StringComparer.Ordinal);
        Assert.Equal(Array.Empty<string>(), unregistered);

        // ...and the mechanism, not only the list: each demanded name resolves to a real policy.
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach (var name in demanded)
        {
            Assert.NotNull(await provider.GetPolicyAsync(name));
        }
    }

    /// <summary>
    /// The guard is only worth its line count if it can go red, so the red condition is constructed
    /// rather than argued: an endpoint demanding an unregistered name is added to the same container the
    /// case reads, and the same collector must both SEE it and reject it. The second assertion is the
    /// consequence being guarded against — no policy exists for that name, which is the throw.
    /// </summary>
    [Fact]
    public async Task An_endpoint_demanding_an_unregistered_name_is_caught_and_has_no_policy()
    {
        const string unregistered = "not.a.registered.permission";
        Assert.False(ErpPermissions.IsKnown(unregistered));

        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("__guard_probe"),
            order: 0,
            new EndpointMetadataCollection(new AuthorizeAttribute(unregistered)),
            displayName: "guard probe");
        var demanded = CollectPolicyNames([endpoint]);

        Assert.Equal(new[] { unregistered }, demanded);
        Assert.True(demanded.Any(name => !ErpPermissions.IsKnown(name)));

        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.Null(await provider.GetPolicyAsync(unregistered));
    }

    private static IReadOnlyList<string> DemandedPolicyNames(IServiceProvider services) =>
        CollectPolicyNames(services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints));

    private static IReadOnlyList<string> CollectPolicyNames(IEnumerable<Endpoint> endpoints) =>
        [.. endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(data => data.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Select(policy => policy!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
}
