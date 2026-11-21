using GoymenERP.Modules.Inventory.Application;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryTransferRegistration
{
    /// <summary>
    /// Owner registration only. The host must also provide <see cref="IInventoryTransferDestinationPortV1"/> and
    /// centrally register the transfer permissions (B-22c receipt, excess and loss names included; the KLN-D22 reverse
    /// transfer reuses the transfer write name) before publishing any route; this method grants nothing.
    /// </summary>
    public static IServiceCollection AddInventoryTransfers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryTransferWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferCommandV1, InventoryTransferCommandService>();
        services.AddScoped<IInventoryTransferQueryV1, InventoryTransferQueryService>();
        services.AddScoped<IInventoryTransferPlanCandidatesV1>(CreateTransferPlanCandidates);
        // LOJ-D20: the read-only list of dispatches whose source scale setting could not be read ("kontrol edilemedi").
        services.AddScoped<IInventoryTransferScaleCheckReviewReaderV1, InventoryTransferScaleCheckReviewReader>();
        services.AddScoped<IInventoryTransferScaleCheckReviewQueryV1, InventoryTransferScaleCheckReviewQueryService>();
        services.AddScoped<IInventoryTransferReceiptWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReceiptReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReceiptCommandV1, InventoryTransferReceiptCommandService>();
        services.AddScoped<IInventoryTransferReceiptQueryV1, InventoryTransferReceiptQueryService>();
        services.AddScoped<IInventoryTransferReceiptDocumentsV1, InventoryTransferReceiptDocuments>();
        services.AddScoped<IInventoryTransferReceiptWorkV1, InventoryTransferReceiptWork>();
        services.AddScoped<IInventoryTransferReverseWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReverseReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReverseCommandV1, InventoryTransferReverseCommandService>();
        services.AddScoped<IInventoryTransferReverseQueryV1, InventoryTransferReverseQueryService>();
        return services;
    }

    private static IInventoryTransferPlanCandidatesV1 CreateTransferPlanCandidates(IServiceProvider provider) =>
        new InventoryTransferPlanCandidates(provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(), provider.GetRequiredService<IErpAuthorityRevalidationService>(),
            provider.GetService<InventoryDbContext>() is { } database ? new SqlInventoryStockPositionReader(database) : null);
}
