using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    public async Task<PurchaseReceiptReversalResponseV1> ReverseGoodsReceiptAsync(Guid orderId, Guid acceptanceId,
        ReversePurchaseReceiptV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.GoodsReceiptPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || acceptanceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseReceiptReversalJsonV1.Validate(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var runner = receiptReversals ?? throw ReceiptUnavailable();
        var envelope = Envelope(scope, metadata, permission!);
        var write = new PurchaseReceiptReversalWriteV1(orderId, acceptanceId, request);
        var hash = PurchaseReceiptReversalHashV1.Compute(write);
        const string command = PurchaseReceiptReversalApiV1.CommandName;
        try
        {
            // The host performs immutable owner replay before mutable target reads. Every owner wait uses the original
            // command's fresh authority fence, including preparation failures before any owner mutation.
            return await ExecuteOwnerAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (PurchasingServiceException exception) when (!exception.IsReplay && ReceiptReversalTerminal(exception))
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            try
            {
                await store.RecordRefusalAsync(envelope, hash, command,
                    new(exception.Status, exception.Code, exception.Errors), cancellationToken);
            }
            catch (PurchasingStoreException concurrent) when (concurrent.Code == PurchasingStoreCodes.ConcurrentChange)
            {
                // Success and terminal refusal share Purchasing.Attempts uniqueness. Re-read the immutable owner
                // response; generic FindReplay would restore today's mutable order even for a historical success.
                return await ExecuteOwnerAsync();
            }
            throw new PurchasingServiceException(exception.Status, exception.Code, exception.Errors,
                isReplay: false, originalCorrelationId: envelope.CorrelationId);
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        async Task<PurchaseReceiptReversalResponseV1> ExecuteOwnerAsync()
        {
            try
            {
                return await runner.ExecuteAsync(scope.CompanyId, write, envelope,
                    token => RequireStillAsync(scope, permission, token), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                await RequireStillAsync(scope, permission, cancellationToken);
                throw;
            }
        }
    }

    private static bool ReceiptReversalTerminal(PurchasingServiceException exception) => exception.Status is 409 or 422
        && exception.Code is not (PurchasingProblemCodesV1.ConcurrentChange or PurchasingStoreCodes.LineBusy
            or PurchaseAcceptanceParticipantCodes.ConcurrentChange or "sales_order_line_busy"
            or "inventory_concurrent_change" or "inventory_goods_receipt_concurrent_change"
            or PurchasingProblemCodesV1.SameKeyPayloadMismatch or "inventory_goods_receipt_reversal_idempotency_conflict"
            or "inventory_goods_receipt_idempotency_conflict");
}
