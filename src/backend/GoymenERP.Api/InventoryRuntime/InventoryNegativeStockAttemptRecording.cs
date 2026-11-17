using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.InventoryRuntime;

internal static class InventoryNegativeStockAttemptRecording
{
    public static async Task AfterRollbackAsync(IServiceProvider services, Exception refusal)
    {
        if (InventoryNegativeStockAttemptEvidenceException.Find(refusal) is null) return;
        await services.GetRequiredService<IInventoryNegativeStockAttemptRecorderV1>()
            .RecordAfterRollbackAsync(refusal, CancellationToken.None);
    }
}
