using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoymenERP.Api.Startup;

internal sealed class DatabaseSchemaCompatibilityHealthCheck(
    IServiceScopeFactory scopeFactory,
    IIdentityAuthorizationDatabaseReadiness identityDatabaseReadiness,
    ProductRuntimeOptions productRuntimeOptions,
    ILogger<DatabaseSchemaCompatibilityHealthCheck> logger) : IHealthCheck
{
    internal const string Name = "database-schema-compatibility";
    internal const string ReadinessTag = "ready";

    private static readonly (string Name, Type ContextType)[] Contexts =
    [
        ("organization", typeof(OrganizationDbContext)),
        ("settings", typeof(SettingsDbContext)),
        ("identity", typeof(IdentityDbContext)),
        ("audit", typeof(AuditDbContext)),
        ("idempotency", typeof(CommandExecutionDbContext)),
        ("counterparty", typeof(CounterpartyDbContext)),
        ("inventory", typeof(InventoryDbContext)),
        ("product", typeof(ProductDbContext)),
        ("product-price-list", typeof(ProductPriceListDbContext)),
        ("group-shared-command", typeof(GroupSharedCommandDbContext)),
        ("finance", typeof(FinanceDbContext)),
        ("sales", typeof(SalesDbContext)),
        ("manufacturing", typeof(ManufacturingDbContext)),
        ("logistics", typeof(LogisticsDbContext)),
        ("purchasing", typeof(PurchasingDbContext)),
        ("accounting", typeof(AccountingDbContext)),
        ("documents", typeof(DocumentsDbContext)),
        ("work", typeof(WorkDbContext)),
    ];

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        foreach (var (name, contextType) in Contexts)
        {
            if (!productRuntimeOptions.IsEnabled && IsProductRuntimeContext(contextType))
            {
                continue;
            }

            try
            {
                var database = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                if (!database.Database.IsRelational()
                    || !await database.Database.CanConnectAsync(cancellationToken))
                {
                    return Unhealthy(
                        "Veritabanı şeması kullanıma hazır değil.",
                        name);
                }

                var sourceMigrations = database.Database
                    .GetMigrations()
                    .ToArray();
                var appliedMigrations = (await database.Database
                        .GetAppliedMigrationsAsync(cancellationToken))
                    .ToArray();
                if (!sourceMigrations.SequenceEqual(
                        appliedMigrations,
                        StringComparer.Ordinal))
                {
                    return Unhealthy(
                        "Veritabanı şeması uygulama kaynağıyla uyumlu değil.",
                        name);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning(
                    "Database schema compatibility check failed for {DatabaseContext}.",
                    name);
                return Unhealthy(
                    "Veritabanı şeması doğrulanamadı.",
                    name);
            }
        }

        try
        {
            await identityDatabaseReadiness.EnsureReadyAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("Identity authorization database readiness check failed.");
            return Unhealthy(
                "Veritabanı okuma tutarlılığı kullanıma hazır değil.",
                "identity-rcsi");
        }

        return HealthCheckResult.Healthy();
    }

    // The Product runtime gate (B-06) owns these contexts; the host neither migrates nor serves them while it is off.
    internal static bool IsProductRuntimeContext(Type contextType) =>
        contextType == typeof(ProductDbContext)
        || contextType == typeof(GroupSharedCommandDbContext)
        || contextType == typeof(ProductPriceListDbContext);

    internal static IReadOnlyList<(string Name, Type ContextType)> CheckedContexts => Contexts;

    private static HealthCheckResult Unhealthy(string description, string context) =>
        HealthCheckResult.Unhealthy(
            description,
            data: new Dictionary<string, object>
            {
                ["context"] = context,
            });
}
