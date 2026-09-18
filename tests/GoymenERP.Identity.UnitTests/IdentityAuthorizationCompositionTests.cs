using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// The composition the fresh-authority guarantee rests on, measured without a database: both
/// authorization ports must resolve to ONE scoped instance, because the revalidation refreshes the
/// very cache the cached port reads. Registering a second instance for the other port would leave
/// each with its own cache and silently restore the stale answer.
/// <para>
/// This is deliberately cheap: <c>AddDbContext</c> does not connect at registration time, so no
/// SQL Server is needed and the check runs in every environment. The real-SQL acceptance test
/// still measures the behaviour end to end, but it is skipped without a connection string.
/// </para>
/// </summary>
public sealed class IdentityAuthorizationCompositionTests
{
    [Fact]
    public void Both_authorization_ports_resolve_to_one_instance_per_scope()
    {
        using var provider = BuildProvider();

        using var request = provider.CreateScope();
        var cached = request.ServiceProvider.GetRequiredService<IErpAuthorizationService>();
        var fresh = request.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>();

        Assert.Same(cached, fresh);
        // Resolving either port twice must not create a second view within the request.
        Assert.Same(cached, request.ServiceProvider.GetRequiredService<IErpAuthorizationService>());
        Assert.Same(fresh, request.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>());
    }

    [Fact]
    public void Each_request_gets_its_own_authorization_view()
    {
        using var provider = BuildProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // Scoped, not singleton: one request's refreshed answers never travel to the next.
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IErpAuthorizationService>(),
            second.ServiceProvider.GetRequiredService<IErpAuthorizationService>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>(),
            second.ServiceProvider.GetRequiredService<IErpAuthorityRevalidationService>());
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to: registration only. A syntactically valid string is enough.
                ["DB_CONNECTION_STRING"] =
                    "Server=composition-test-not-connected;Database=composition;Integrated Security=true",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new CompositionUser());
        services.AddIdentityModule(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class CompositionUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public string? UserId => null;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => false;
    }
}
