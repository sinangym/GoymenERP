namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    // Only the active exact native approval marker performs a fresh fence.
    // All other existing Accounting callers retain their original read behaviour.
    private async Task OwnedPostingReadFenceAsync(CancellationToken token)
    {
        await ManufacturingCostDifferenceApprovalReadFenceAsync(token);
        await InvoiceLineCogsApprovalReadFenceAsync(token);
        await InventoryCostApprovalReadFenceAsync(token);
        await InventoryCostCancellationApprovalReadFenceAsync(token);
    }
}
