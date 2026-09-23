using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryParentRollRegistration
{
    /// <summary>Owner ports only. The central host publishes routes; no grants, initializer or writes here.</summary>
    public static IServiceCollection AddInventoryParentRollReferences(this IServiceCollection services)
    {
        services.AddScoped<InventoryParentRollService>(sp => new(
            sp.GetRequiredService<ICompanyContext>(), sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<IErpAuthorityRevalidationService>(),
            sp.GetService<InventoryDbContext>() is { } db ? new SqlInventoryParentRollReader(db) : null));
        services.AddScoped<IInventoryParentRollCandidatesV1>(sp => sp.GetRequiredService<InventoryParentRollService>());
        services.AddScoped<IInventoryParentRollValidationV1>(sp => sp.GetRequiredService<InventoryParentRollService>());
        return services;
    }
}
