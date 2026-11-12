using GoymenERP.Api.Startup;
using GoymenERP.Modules.Documents.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DatabaseSchemaCompatibilityHealthCheckTests
{
    [Fact]
    public void CheckedContexts_CoverSettingsDocumentsAndPriceLists_Once()
    {
        var contexts = DatabaseSchemaCompatibilityHealthCheck.CheckedContexts;

        Assert.Contains(contexts, item => item.ContextType == typeof(SettingsDbContext));
        Assert.Contains(contexts, item => item.ContextType == typeof(DocumentsDbContext));
        Assert.Contains(contexts, item => item.ContextType == typeof(ProductPriceListDbContext));
        Assert.Equal(contexts.Count, contexts.Select(item => item.ContextType).Distinct().Count());
        Assert.Equal(contexts.Count, contexts.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(contexts, item => Assert.True(typeof(DbContext).IsAssignableFrom(item.ContextType)));
    }

    [Fact]
    public void ProductRuntimeGate_SkipsOnlyProductContexts()
    {
        Assert.True(DatabaseSchemaCompatibilityHealthCheck.IsProductRuntimeContext(typeof(ProductPriceListDbContext)));
        Assert.False(DatabaseSchemaCompatibilityHealthCheck.IsProductRuntimeContext(typeof(SettingsDbContext)));
        Assert.False(DatabaseSchemaCompatibilityHealthCheck.IsProductRuntimeContext(typeof(DocumentsDbContext)));
    }
}
