using System.Security.Cryptography;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Shell;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.ProductRuntime;

public static class ProductRuntimeExtensions
{
    public static IServiceCollection AddProductRuntime(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connection = configuration["DB_CONNECTION_STRING"] ?? configuration.GetConnectionString("GoymenErp")
            ?? throw new InvalidOperationException("Product requires the existing SQL connection configuration.");
        ProductPilotIssuerBinding? issuer = null;
        byte[]? key = null;
        if (!OpenApiDocumentGenerationMode.IsActive)
        {
            var local = configuration["ProductRuntime:IssuerLocalUserId"];
            var external = configuration["ProductRuntime:IssuerExternalIdentityId"];
            if (local is not null || external is not null)
            {
                if (!Guid.TryParseExact(local, "D", out var localId) || localId == Guid.Empty
                    || !Guid.TryParseExact(external, "D", out var externalId) || externalId == Guid.Empty)
                    throw new InvalidOperationException("The complete verified Product pilot issuer binding is required.");
                issuer = new ProductPilotIssuerBinding(localId, externalId);
                var encoded = configuration["ProductRuntime:CursorSigningKeyBase64"];
                if (encoded is not null) key = Convert.FromBase64String(encoded);
                else if (environment.IsDevelopment() || environment.IsEnvironment("Testing")) key = RandomNumberGenerator.GetBytes(32);
                if (key is null || key.Length < 32)
                    throw new InvalidOperationException("Configured Product runtime requires cursor signing material.");
            }
        }
        services.AddSingleton(new ProductRuntimeOptions(connection, issuer, key));
        services.AddDbContext<ProductDbContext>(options => options.UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "product")));
        services.AddDbContext<GroupSharedCommandDbContext>(options => options.UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__GroupSharedCommandMigrationsHistory", "idempotency")));
        services.AddScoped(CreateIdentityAuthority);
        services.AddScoped<IGroupSharedMasterAuthorizationService, ProductHostAuthorizationService>();
        services.AddScoped<IGroupSharedCommandTerminalReader, SqlGroupSharedCommandTerminalStore>();
        services.AddScoped<IGroupSharedCommandReplayResolver, GroupSharedCommandReplayResolver>();
        services.AddScoped<GroupSharedProductAuditReader>();
        services.AddScoped<ProductCommandGateway>();
        services.AddScoped<ProductReadStore>();
        services.AddSingleton<ProductCursorCodec>(provider =>
        {
            var settings = provider.GetRequiredService<ProductRuntimeOptions>();
            return new ProductCursorCodec(settings.CursorSigningKey ?? throw new InvalidOperationException("Product cursor is unconfigured."),
                TimeProvider.System, TimeSpan.FromSeconds(settings.CursorLifetimeSeconds));
        });
        services.AddScoped<ProductReader>();
        services.AddScoped<ProductService>();
        services.AddScoped<IProductDraftCommandRunner, ProductSqlCommandRunner>();
        services.TryAddSingleton<IProductCommandAttemptObserver, NullProductCommandAttemptObserver>();
        services.AddScoped<IProductService>(provider => provider.GetRequiredService<ProductRuntimeOptions>().IsEnabled
            ? provider.GetRequiredService<ProductService>() : new UnconfiguredProductService());
        // Product's four screens in the shell, granted only from the global Product authority.
        services.AddSingleton<IShellCapabilityProvider, ProductShellCapabilityProvider>();
        // C3: the Product owner's operational-selection runtimes (sales and stock), composed as the
        // owner decided. The accepted GDC band/coil rule set over the released family definition read
        // from Product's own registry (PRD-FAM-1, #334): while the registry holds no released family,
        // every otherwise-valid selection is 503 product_family_policy_unavailable, as before. No grant,
        // no Settings registration of its own; without the configured runtime or a registered Settings
        // provider both ports answer 503.
        services.TryAddScoped<IProductFamilyDefinitionSourceV2, SqlProductFamilyDefinitionSource>();
        services.TryAddScoped<IProductFamilyRequirementPolicyV1, ProductFamilyRequirementPolicyV2>();
        services.AddScoped<IProductSalesEligibilityV1>(provider => CreateEligibility<IProductSalesEligibilityV1>(provider,
            (company, user, authority, records, policy, units) =>
                new ProductSalesEligibilityService(company, user, authority, records, policy, units),
            () => new UnconfiguredProductEligibility()));
        services.AddScoped<IProductSalesCandidateReader, SqlProductSalesCandidateReader>();
        services.AddScoped<IProductSalesCandidatesV1, ProductSalesCandidateService>();
        services.AddScoped<IProductStockEligibilityV1>(provider => CreateEligibility<IProductStockEligibilityV1>(provider,
            (company, user, authority, records, policy, units) =>
                new ProductStockEligibilityService(company, user, authority, records, policy, units),
            () => new UnconfiguredProductEligibility()));
        return services;
    }

    private static T CreateEligibility<T>(IServiceProvider provider,
        Func<ICompanyContext, ICurrentUser, IErpAuthorityRevalidationService, IProductSalesRecordReader,
            IProductFamilyRequirementPolicyV1, ProductUnitProfileAdapter, T> configured,
        Func<T> unconfigured)
    {
        var settings = provider.GetService<ISettingsPrerequisiteProvider>();
        if (!provider.GetRequiredService<ProductRuntimeOptions>().IsEnabled || settings is null)
            return unconfigured();
        var company = provider.GetRequiredService<ICompanyContext>();
        return configured(company, provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IErpAuthorityRevalidationService>(),
            provider.GetRequiredService<ProductReadStore>(),
            provider.GetRequiredService<IProductFamilyRequirementPolicyV1>(),
            new ProductUnitProfileAdapter(settings, company));
    }

    private static SqlProductMasterAuthorizationService CreateIdentityAuthority(IServiceProvider provider) =>
        new SqlProductMasterAuthorizationService(provider.GetRequiredService<IdentityDbContext>(),
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ProductRuntimeOptions>().Issuer);

    public static async Task InitializeProductRuntimeAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (!services.GetRequiredService<ProductRuntimeOptions>().IsEnabled) return;
        await using var scope = services.CreateAsyncScope();
        var product = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var terminal = scope.ServiceProvider.GetRequiredService<GroupSharedCommandDbContext>();
        // Owned migrations only; no grant, company account, Product row or code group is seeded.
        await product.Database.MigrateAsync(cancellationToken);
        await terminal.Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>Both eligibility ports when the Product runtime or the Settings provider is absent: 503, never a positive answer.</summary>
internal sealed class UnconfiguredProductEligibility : IProductSalesEligibilityV1, IProductStockEligibilityV1
{
    /// <summary>A host-level 503 code; the Product owner documents it on the ports.</summary>
    public const string UnavailableCode = "product_runtime_unconfigured";

    public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken) =>
        Unavailable<ProductSalesEligibilityV1>(cancellationToken);

    public Task<ProductStockEligibilityV1> EvaluateAsync(ProductStockSelectionV1 selection, CancellationToken cancellationToken) =>
        Unavailable<ProductStockEligibilityV1>(cancellationToken);

    private static Task<T> Unavailable<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<T>(new ProductOperationException(UnavailableCode, 503));
    }
}

internal sealed class UnconfiguredProductService : IProductService
{
    private static UnauthorizedAccessException Denied() => new UnauthorizedAccessException("Explicit global Product authority is unconfigured.");
    public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken) => throw Denied();
    public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw Denied();
    public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) => throw Denied();
    public Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query, CancellationToken cancellationToken) => throw Denied();
    public Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw Denied();
    public Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw Denied();
    public Task<ProductMutationResponse> ActivateAsync(Guid id, ActivateProductRequest request, string idempotencyKey, CancellationToken cancellationToken) => throw Denied();
}
