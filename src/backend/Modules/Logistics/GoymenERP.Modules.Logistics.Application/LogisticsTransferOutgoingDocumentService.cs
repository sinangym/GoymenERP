using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Transfers;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// Authority-fenced owner service for the I01.2 source package. It intentionally has no HTTP route;
/// the later central transfer host owns route/permission exposure and Inventory movement timing.
/// </summary>
public sealed class LogisticsTransferOutgoingDocumentService(
    ILogisticsTransferOutgoingDocumentScopeV1 scopes,
    ILogisticsTransferOutgoingDocumentStoreV1 store,
    LogisticsTransferOutgoingDocumentBindingsV1 bindings,
    ICorrelationContext correlation,
    TimeProvider timeProvider) : ILogisticsTransferOutgoingDocumentServiceV1
{
    public async Task<TransferOutgoingDocumentResponseV1> CreateDraftAsync(
        CreateTransferOutgoingDocumentV1 request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await scopes.RequireAsync(bindings.CreatePermission, cancellationToken);
        var envelope = Envelope(scope, bindings.CreatePermission, metadata);
        try
        {
            var document = TransferOutgoingDocument.Create(scope.CompanyId, request.DocumentId,
                request.TransferId, request.OrderedTransferLineIds, request.EDispatchNumber,
                scope.ActorId, timeProvider.GetUtcNow());
            var hash = LogisticsRequestHash.Compute(LogisticsTransferOutgoingDocumentCommandsV1.CreateDraft,
                request.DocumentId, request);
            var result = await store.StoreDraftAsync(envelope, hash, document,
                token => scopes.RequireStillAsync(scope, bindings.CreatePermission, token), cancellationToken);
            return Response(result);
        }
        catch (TransferOutgoingDocumentRuleException exception)
        {
            throw Rule(exception);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }
        catch (ArgumentException)
        {
            throw new LogisticsTransferOutgoingDocumentServiceException(422, "validation_failed");
        }
    }

    public Task<TransferOutgoingDocumentResponseV1> FinalizeAsync(Guid documentId,
        FinalizeTransferOutgoingDocumentV1 request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(documentId, request, metadata, bindings.FinalizePermission,
            LogisticsTransferOutgoingDocumentCommandsV1.Finalize, cancellationToken);
    }

    public Task<TransferOutgoingDocumentResponseV1> CancelAsync(Guid documentId,
        CancelTransferOutgoingDocumentV1 request, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MutateAsync(documentId, request, metadata, bindings.CancelPermission,
            LogisticsTransferOutgoingDocumentCommandsV1.Cancel, cancellationToken);
    }

    private async Task<TransferOutgoingDocumentResponseV1> MutateAsync(Guid documentId, object request,
        LogisticsCommandMetadata metadata, string permission, string command, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new LogisticsTransferOutgoingDocumentServiceException(422, "validation_failed");
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await scopes.RequireAsync(permission, cancellationToken);
        var envelope = Envelope(scope, permission, metadata);
        var hash = LogisticsRequestHash.Compute(command, documentId, request);
        try
        {
            var result = await store.MutateAsync(envelope, hash, command, documentId,
                (document, now) =>
                {
                    if (request is FinalizeTransferOutgoingDocumentV1 finalize)
                        document.Finalize(finalize.ExpectedVersion, scope.ActorId, now);
                    else if (request is CancelTransferOutgoingDocumentV1 cancel)
                        document.Cancel(cancel.ExpectedVersion, scope.ActorId, cancel.Reason, now);
                    else
                        throw new InvalidOperationException("Unsupported transfer outgoing-document transition.");
                },
                token => scopes.RequireStillAsync(scope, permission, token), cancellationToken);
            return Response(result);
        }
        catch (TransferOutgoingDocumentRuleException exception)
        {
            throw Rule(exception);
        }
        catch (LogisticsStoreException exception)
        {
            throw Store(exception);
        }
        catch (ArgumentException)
        {
            throw new LogisticsTransferOutgoingDocumentServiceException(422, "validation_failed");
        }
    }

    private LogisticsWriteEnvelope Envelope(LogisticsTransferOutgoingDocumentScopeV1 scope,
        string permission, LogisticsCommandMetadata metadata) => new(scope.ActorId, permission, scope.RoleCodes,
        metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
        correlation.ClientIpAddress, correlation.UserAgent);

    private static TransferOutgoingDocumentResponseV1 Response(TransferOutgoingDocumentStoreResultV1 result) =>
        new(View(result.Document), result.Outcome == LogisticsWriteOutcome.Replayed,
            result.OriginalCorrelationId);

    private static TransferOutgoingDocumentViewV1 View(TransferOutgoingDocument item) => new(
        item.CompanyId, item.DocumentId, item.TransferId, item.OrderedTransferLineIds, item.EDispatchNumber,
        item.Status.ToString(), item.Version, item.CreatedBy, item.CreatedAtUtc, item.EffectiveBy,
        item.EffectiveAtUtc, item.CancelledBy, item.CancelledAtUtc, item.CancellationReason);

    private static LogisticsTransferOutgoingDocumentServiceException Rule(
        TransferOutgoingDocumentRuleException exception) => new(409, exception.Code);

    private static LogisticsTransferOutgoingDocumentServiceException Store(LogisticsStoreException exception) =>
        exception.Code switch
        {
            LogisticsStoreCodes.SameKeyPayloadMismatch => new(409, LogisticsStoreCodes.SameKeyPayloadMismatch),
            LogisticsStoreCodes.ConcurrentChange => new(409, LogisticsTransferOutgoingDocumentCodesV1.VersionConflict),
            LogisticsTransferOutgoingDocumentCodesV1.NotFound => new(404, LogisticsTransferOutgoingDocumentCodesV1.NotFound),
            LogisticsTransferOutgoingDocumentCodesV1.TransferAlreadyBound => new(409,
                LogisticsTransferOutgoingDocumentCodesV1.TransferAlreadyBound),
            _ => new(503, "logistics_transfer_outgoing_document_unavailable"),
        };
}
