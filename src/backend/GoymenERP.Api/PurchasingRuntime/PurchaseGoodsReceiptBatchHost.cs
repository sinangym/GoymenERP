using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// ALC-S16 (F-027; KLN-D43): the bulk goods receipt host. It owns ONE transaction on the scoped Purchasing context: the batch row,
/// its attempt and its audit (the store's single save) and every label's acceptance + stock rows commit together or not at all.
/// Each label borrows a FRESH pair of the existing single-use receipt participants (the single-label receipt is unchanged); every
/// enlisted participant is disposed after the commit or the rollback. An Inventory refusal carries its label's field
/// (<c>labels[i]</c>); a label code Inventory already holds answers <see cref="PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict"/>
/// on <c>labels[i].labelCode</c> (KLN-D41).
/// </summary>
internal sealed class PurchaseGoodsReceiptBatchHost(
    PurchasingDbContext purchasing,
    IOrganizationLocationStatusReaderV1 locationStatus,
    IServiceProvider services,
    TimeProvider clock) : IPurchaseGoodsReceiptBatchTransactionV1
{
    public async Task<T> ExecuteAsync<T>(Guid companyId,
        Func<IPurchaseGoodsReceiptBatchParticipantsV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new InvalidOperationException("Bulk goods receipt requires a company.");
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        try
        {
            await using var transaction = await purchasing.Database.BeginTransactionAsync(cancellationToken);
            var enlisted = new BatchParticipants(companyId, purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(),
                services.GetRequiredService<ISettingsPrerequisiteProvider>(), locationStatus,
                services.GetRequiredService<IInventoryGoodsReceiptFutureSupplyPortV1>(),
                services.GetRequiredService<IInventoryFutureAllocationReleasePortV1>(), clock);
            try
            {
                var result = await operation(enlisted, cancellationToken);
                await beforeCommit(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await RollbackQuietlyAsync(transaction);
                purchasing.ChangeTracker.Clear();
                throw;
            }
            finally
            {
                await enlisted.DisposeAsync();
            }
        }
        catch (InventoryGoodsReceiptException exception)
        {
            throw new PurchasingServiceException(exception.Status, exception.Code);
        }
    }

    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch { /* Preserve the original operation failure. */ }
    }

    /// <summary>Enlists one fresh participant pair per label, in label order; each label index is enlisted at most once.</summary>
    private sealed class BatchParticipants(Guid companyId, System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, ISettingsPrerequisiteProvider settings,
        IOrganizationLocationStatusReaderV1 locations, IInventoryGoodsReceiptFutureSupplyPortV1 futureSupply,
        IInventoryFutureAllocationReleasePortV1 futureReleases, TimeProvider clock) : IPurchaseGoodsReceiptBatchParticipantsV1, IAsyncDisposable
    {
        private readonly List<IAsyncDisposable> owned = [];
        private readonly HashSet<int> labels = [];

        public async Task<IPurchaseGoodsReceiptParticipantsV1> EnlistLabelAsync(int labelIndex, CancellationToken cancellationToken)
        {
            if (labelIndex < 0 || !labels.Add(labelIndex))
                throw new InvalidOperationException("Each bulk receipt label enlists its participants once.");
            var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, companyId, cancellationToken);
            owned.Add(purchase);
            var inventory = await InventoryGoodsReceiptParticipant.EnlistAsync(connection, transaction, companyId, settings,
                cancellationToken, clock);
            owned.Add(inventory);
            return new LabelParticipants(labelIndex,
                new PurchaseGoodsReceiptHost.Participants(companyId, purchase, inventory, locations, futureSupply,
                    futureReleases, connection, transaction));
        }

        public async ValueTask DisposeAsync()
        {
            for (var index = owned.Count - 1; index >= 0; index--)
            {
                try { await owned[index].DisposeAsync(); }
                catch { /* Disposal never hides the operation's own outcome. */ }
            }

            owned.Clear();
        }
    }

    /// <summary>Adds the label's field to an Inventory refusal; the stages themselves are the unchanged single-label participants.</summary>
    private sealed class LabelParticipants(int labelIndex, IPurchaseGoodsReceiptParticipantsV1 inner) : IPurchaseGoodsReceiptParticipantsV1
    {
        public Task<PurchaseReceiptStockRequestV1?> ReadOriginalAsync(Guid acceptanceId, PurchasingWriteEnvelope envelope,
            CancellationToken cancellationToken) =>
            MappedAsync(() => inner.ReadOriginalAsync(acceptanceId, envelope, cancellationToken));

        public Task<PurchaseReceiptFactsV1> ResolveFirstAsync(PurchaseReceiptSourceV1 source, Guid locationId,
            CancellationToken cancellationToken) =>
            MappedAsync(() => inner.ResolveFirstAsync(source, locationId, cancellationToken));

        public Task<PurchaseAcceptanceStageResultV1> StagePurchaseAsync(PurchaseAcceptanceStageRequestV1 request,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken) =>
            inner.StagePurchaseAsync(request, envelope, cancellationToken);

        public Task<PurchaseReceiptStockResultV1> StageInventoryAsync(PurchaseReceiptStockRequestV1 request,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken) =>
            MappedAsync(() => inner.StageInventoryAsync(request, envelope, cancellationToken));

        private async Task<TResult> MappedAsync<TResult>(Func<Task<TResult>> stage)
        {
            try
            {
                return await stage();
            }
            catch (InventoryGoodsReceiptException exception)
                when (exception.Code == InventoryGoodsReceiptCodesV1.LabelAlreadyRegistered)
            {
                throw new PurchasingServiceException(StatusCodes.Status409Conflict, PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict,
                    [new($"labels[{labelIndex}].labelCode", PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict)]);
            }
            catch (InventoryGoodsReceiptException exception)
            {
                throw new PurchasingServiceException(exception.Status, exception.Code, [new($"labels[{labelIndex}]", exception.Code)]);
            }
        }
    }
}
