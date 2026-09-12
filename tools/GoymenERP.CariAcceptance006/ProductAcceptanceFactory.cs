using System.Net;
using System.Security.Cryptography;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Product.UnitTests.SqlAcceptance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GoymenERP.CariAcceptance006;

public sealed class ProductAcceptanceFactory(ProductEnvironmentSettings settings) : WebApplicationFactory<global::Program>
{
    public const string Revision = "sha256:h3eow9s5L3gYs0heRdgllKTw5iTNcXXLokOTL9QCVMY";
    public async Task StartLoopbackAsync(CancellationToken cancellationToken)
    {
        _ = settings.VerifySource();
        UseKestrel(options => options.Listen(IPAddress.Loopback, ProductEnvironmentSettings.ApiPort));
        StartServer();
        if (Services.GetRequiredService<ShellCapabilityRegistry>().Revision != Revision)
            throw new InvalidOperationException("Product fixture capability revision differs from the reviewed declarations.");
        await using var scope = Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var operation in Enum.GetValues<ProductMasterOperation>())
            identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ProductEnvironmentSettings.ActorId,
                ProductEnvironmentSettings.SystemRoleId, operation, ProductEnvironmentSettings.ActorId, ProductEnvironmentSettings.ActorExternalId,
                Guid.NewGuid(), "Synthetic isolated Product browser fixture; not Sinan or production authority",
                DateTimeOffset.UtcNow.AddMinutes(-1), null));
        await identity.SaveChangesAsync(cancellationToken);
        await using var sql = new SqlConnection(settings.DatabaseConnection());
        await sql.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await sql.BeginTransactionAsync(cancellationToken);
        var fixture = ProductEnvironmentEvidence.Fixture(settings);
        await fixture.SeedCodeGroupsAsync(sql, transaction,
            [ProductCodeGroupRow.Define(ProductEnvironmentSettings.CodeGroupId, "Isolated Product browser code group", "PSQL")], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.Combine(SourceBinding.ProductWorktree, "src/backend/GoymenERP.Api"));
        var connection = settings.DatabaseConnection();
        builder.UseSetting("DB_CONNECTION_STRING", connection);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_PROVIDER"] = "SqlServer", ["Authentication:Mode"] = "DevelopmentBootstrap",
            ["BootstrapIdentity:HeaderName"] = "X-Development-User",
            ["BootstrapIdentity:Users:0:UserId"] = ProductEnvironmentSettings.UnauthorizedActor,
            ["BootstrapIdentity:Users:0:DisplayName"] = "Synthetic Product company-only actor",
            ["BootstrapIdentity:Users:1:UserId"] = "company-b-user",
            ["BootstrapIdentity:Users:1:DisplayName"] = "Synthetic Product company B actor",
            ["BootstrapIdentity:Users:2:UserId"] = ProductEnvironmentSettings.Actor,
            ["BootstrapIdentity:Users:2:DisplayName"] = "Synthetic explicit global Product actor",
            ["OrganizationSeed:Companies:0:LegalName"] = "Synthetic Product company A",
            ["OrganizationSeed:Companies:0:Vkn"] = "1111111111",
            ["OrganizationSeed:Companies:0:IsPlaceholder"] = "true",
            ["OrganizationSeed:Companies:0:InvoiceCapable"] = "false",
            ["OrganizationSeed:Companies:1:LegalName"] = "Synthetic Product company B",
            ["OrganizationSeed:Companies:1:Vkn"] = "1111111111",
            ["OrganizationSeed:Companies:1:IsPlaceholder"] = "true",
            ["OrganizationSeed:Companies:1:InvoiceCapable"] = "false",
        }));
        builder.ConfigureServices(services =>
        {
            var providers = services.Where(s => s.ServiceType == typeof(IShellCapabilityProvider)).ToArray();
            if (providers.Length != 1 || providers[0].ImplementationType != typeof(CounterpartyShellCapabilityProvider)
                || providers[0].Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException("Product fixture cannot replace an unexpected provider.");
            services.Remove(providers[0]);
            services.AddSingleton<IShellCapabilityProvider, ProductAcceptanceProvider>();
            services.RemoveAll<ShellCapabilityAuthorityCatalog>();
            services.AddSingleton(new ShellCapabilityAuthorityCatalog([Revision]));
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ProductEnvironmentSettings.ActorId, ProductEnvironmentSettings.ActorExternalId),
                RandomNumberGenerator.GetBytes(32)));
        });
        // Same-origin Vite proxy: no added CORS origin or changed normal policy.
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

public sealed class ProductAcceptanceProvider : IShellCapabilityProvider
{
    public string Owner => "PRODUCT";
    public string ContractVersion => "PRODUCT-TEST-CAPABILITY-V1";
    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        Declare("create", "/urun/yeni", GroupSharedMasterOperation.DraftCreate, false),
        Declare("detail", "/urun/:productId", GroupSharedMasterOperation.Read, true),
        Declare("edit", "/urun/:productId/duzenle", GroupSharedMasterOperation.DraftBasicEdit, true),
        Declare("list", "/urun", GroupSharedMasterOperation.Read, false),
    ];
    private static ShellCapabilityDeclaration Declare(string action, string path, GroupSharedMasterOperation operation, bool parameterized) =>
        new("product.items." + action, "product.items." + action, path, "PRODUCT-TEST-" + action.ToUpperInvariant(),
            parameterized ? ShellCapabilityRoutePrecedence.ParameterizedAfterStatic : ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.AnyGrantedScope, [], true, operation);
}
