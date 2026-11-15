using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public static class InventoryTransferRegistration
{
    /// <summary>
    /// Owner registration only. The host must also provide <see cref="IInventoryTransferDestinationPortV1"/> and
    /// centrally register the transfer permissions (B-22c receipt, excess and loss names included) before publishing any
    /// route; this method grants nothing.
    /// </summary>
    public static IServiceCollection AddInventoryTransfers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInventoryTransferWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferCommandV1, InventoryTransferCommandService>();
        services.AddScoped<IInventoryTransferQueryV1, InventoryTransferQueryService>();
        services.AddScoped<IInventoryTransferReceiptWriterV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReceiptReaderV1>(provider => provider.GetRequiredService<InventoryStore>());
        services.AddScoped<IInventoryTransferReceiptCommandV1, InventoryTransferReceiptCommandService>();
        services.AddScoped<IInventoryTransferReceiptQueryV1, InventoryTransferReceiptQueryService>();
        return services;
    }
}
