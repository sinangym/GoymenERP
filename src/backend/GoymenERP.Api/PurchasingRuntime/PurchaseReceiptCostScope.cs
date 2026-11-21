using System.Data.Common;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Receipt cost evidence observes the acceptance just staged in the same owner transaction.</summary>
internal sealed class PurchaseReceiptCostScope(IPurchaseReceiptCostProofV1 proof, IInventoryPurchaseReceiptCostSourceV1 source)
    : IAsyncDisposable
{
    public IInventoryPurchaseReceiptCostSourceV1 Source { get; } = source;

    public static async Task<PurchaseReceiptCostScope> EnlistAsync(DbConnection connection, DbTransaction transaction,
        Guid companyId, IServiceProvider services, CancellationToken cancellationToken)
    {
        var proof = await PurchaseReceiptCostProof.EnlistAsync(connection, transaction, companyId, cancellationToken);
        try
        {
            return new(proof, new PurchasingOwnerInventoryReceiptCostSource(proof,
                services.GetRequiredService<IProductPurchaseUnitConversionV1>(),
                services.GetRequiredService<ISettingsExchangeRateResolverV1>(),
                services.GetRequiredService<ICompanyContext>(), services.GetRequiredService<ICurrentUser>()));
        }
        catch { await proof.DisposeAsync(); throw; }
    }

    public ValueTask DisposeAsync() => proof.DisposeAsync();
}
