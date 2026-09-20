using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

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
/// WHAT MAKES THE SET COMPLETE, and the dependency that carries it: this file pins only that the
/// collection is non-empty. That the enumeration sees EVERY route is measured by
/// <c>OpenApiContractGenerationTests.EndpointDataSource_MatchesTheGeneratedContractExactly</c>, which
/// walks the identical <c>GetServices&lt;EndpointDataSource&gt;()</c> path and holds it to exact set
/// equality against the committed contract. If that test ever changes mechanism, this file's
/// completeness argument disappears with it — silently — so the dependency is named here rather than
/// assumed.
///
/// The finding behind this guard is ORG's (#373 review, P2-1); FİNANS's review of #375 is why the red
/// case goes through the container instead of around it.
/// </summary>
public sealed class EndpointAuthorizationPolicyRegistrationTests
{
    private const string UnregisteredName = "not.a.registered.permission";

    [Fact]
    public async Task No_endpoint_demands_a_name_the_registry_does_not_know()
    {
        await using var factory = new GoymenErpApiFactory();
        var demanded = await DemandedPolicyNamesAsync(factory);

        // A rule that filtered nothing would pass vacuously: the application really does name policies.
        Assert.NotEmpty(demanded);

        var unregistered = demanded.Where(name => !ErpPermissions.IsKnown(name)).Order(StringComparer.Ordinal).ToArray();
        // Assert.True with the names rather than Assert.Empty: the failure has to say WHICH name, since
        // the fix is to register it in ErpPermissions (or to stop demanding it), not to find it first.
        Assert.True(
            unregistered.Length == 0,
            "These endpoints demand policies the permission registry never built, so each is a 500 at "
            + $"request time, not a refusal: {string.Join(", ", unregistered)}. Register the name in "
            + "ErpPermissions (All, and CompanyWideOnly if it is company-wide), or stop demanding it.");
    }

    /// <summary>
    /// A separate case from the one above, because it is a separate defect: the registration loop could
    /// change and leave a name registered that still produces no policy. Folded into one case, this
    /// would never run on the day the other assertion failed.
    /// </summary>
    [Fact]
    public async Task Every_demanded_name_resolves_to_a_real_policy()
    {
        await using var factory = new GoymenErpApiFactory();
        var demanded = await DemandedPolicyNamesAsync(factory);
        Assert.NotEmpty(demanded);

        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach (var name in demanded)
        {
            Assert.NotNull(await provider.GetPolicyAsync(name));
        }
    }

    /// <summary>
    /// The guard is worth its line count only if it can go red, so the red condition is constructed
    /// rather than argued — and it is constructed THROUGH THE CONTAINER.
    ///
    /// The first version of this case handed a hand-built endpoint straight to the collector, which
    /// proved only that a policy name can be read off metadata. That was never the doubtful half. The
    /// doubtful half is the DI enumeration — <c>GetServices&lt;EndpointDataSource&gt;()</c> — and the
    /// old case walked around it while its comment claimed otherwise. That is the same blind spot this
    /// guard exists to prevent, reappearing in the demonstration instead of in the placement: a proof
    /// can certify what it does not exercise exactly as a location can.
    /// </summary>
    [Fact]
    public async Task An_endpoint_registered_with_an_unregistered_name_is_caught_through_the_container()
    {
        Assert.False(ErpPermissions.IsKnown(UnregisteredName));

        await using var baseFactory = new GoymenErpApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<EndpointDataSource>(new ProbeEndpointDataSource(UnregisteredName))));

        var demanded = await DemandedPolicyNamesAsync(factory);

        // The same collector, over the same DI path the green cases use, must SEE the probe...
        Assert.Contains(UnregisteredName, demanded);
        // ...and reject it, which is the assertion the green case would then fail on.
        Assert.Contains(demanded, name => !ErpPermissions.IsKnown(name));
        // ...and the consequence itself: no policy exists for that name, which is the throw.
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.Null(await provider.GetPolicyAsync(UnregisteredName));
    }

    private static async Task<IReadOnlyList<string>> DemandedPolicyNamesAsync(WebApplicationFactory<Program> factory)
    {
        // The host is built lazily; a real request is the cheapest way to be sure the endpoints exist
        // before they are read.
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        return CollectPolicyNames(factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints));
    }

    private static IReadOnlyList<string> CollectPolicyNames(IEnumerable<Endpoint> endpoints) =>
        [.. endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(data => data.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Select(policy => policy!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>One endpoint, demanding one policy name, registered like any other data source.</summary>
    private sealed class ProbeEndpointDataSource(string policyName) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints { get; } =
        [
            new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("__endpoint_policy_guard_probe"),
                order: 0,
                new EndpointMetadataCollection(new AuthorizeAttribute(policyName)),
                displayName: "endpoint policy guard probe"),
        ];

        // The probe never changes, so a token that never fires. NullChangeToken is not public.
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
