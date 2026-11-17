using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    public async Task<PurchaseReceiptReversalBatchResponseV1> ReverseGoodsReceiptsAsync(Guid orderId, ReversePurchaseReceiptsV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.GoodsReceiptPermission; var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseReceiptReversalBatchJsonV1.Validate(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var runner = receiptReversalBatches ?? throw ReceiptUnavailable();
        var envelope = Envelope(scope, metadata, permission!); var write = new PurchaseReceiptReversalBatchWriteV1(orderId, request);
        var hash = PurchaseReceiptReversalBatchHashV1.Compute(write); const string command = PurchaseReceiptReversalBatchApiV1.CommandName;
        try { return await ExecuteOwnerAsync(); }
        catch (OperationCanceledException) { throw; }
        catch (PurchasingServiceException exception) when (!exception.IsReplay && ReceiptReversalTerminal(exception))
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            try
            {
                await store.RecordRefusalAsync(envelope, hash, command,
                    new(exception.Status, exception.Code, exception.Errors, exception.Detail), cancellationToken);
            }
            catch (PurchasingStoreException concurrent) when (concurrent.Code == PurchasingStoreCodes.ConcurrentChange)
            { return await ExecuteOwnerAsync(); }
            throw new PurchasingServiceException(exception.Status, exception.Code, exception.Errors,
                isReplay: false, originalCorrelationId: envelope.CorrelationId, detail: exception.Detail);
        }
        catch { await RequireStillAsync(scope, permission, cancellationToken); throw; }

        async Task<PurchaseReceiptReversalBatchResponseV1> ExecuteOwnerAsync()
        {
            try
            {
                return await runner.ExecuteBatchAsync(scope.CompanyId, write, envelope,
                    token => RequireStillAsync(scope, permission, token), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch { await RequireStillAsync(scope, permission, cancellationToken); throw; }
        }
    }
}
