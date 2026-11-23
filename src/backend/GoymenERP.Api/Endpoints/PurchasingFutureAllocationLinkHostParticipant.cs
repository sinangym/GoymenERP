using System.Data;
using System.Data.Common;
using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-017 §3.5 F17-4 (ALS-D83/ALS-D85): the host's answer to <see cref="IPurchaseFutureAllocationLinkParticipantV1"/> over the
/// Inventory owner's release port. Purchasing calls it inside its own transaction after it took every general line lock of the order
/// (ascending); the owner re-takes the purchase line lock re-entrantly, checks the expected version and writes the release (allocation,
/// ledger, audit, outbox) in the same transaction. Every answer is checked against the context company, the order and the asked lines;
/// another company's, order's or line's view is never shown or released on its word (503). It lives in the host because neither
/// Inventory nor Purchasing references the other.
/// </summary>
public sealed class PurchasingFutureAllocationLinkHostParticipant(
    IInventoryFutureAllocationReleasePortV1 releases,
    ICompanyContext companyContext) : IPurchaseFutureAllocationLinkParticipantV1
{
    private const int UnavailableStatus = 503;
    private const int ConflictStatus = 409;

    public async Task<IReadOnlyList<PurchaseFutureAllocationLinkV1>> ListActiveAsync(IDbTransaction transaction, Guid purchaseOrderId,
        IReadOnlyList<Guid> purchaseLineIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(purchaseLineIds);
        cancellationToken.ThrowIfCancellationRequested();
        var (connection, dbTransaction) = Bound(transaction);
        if (purchaseOrderId == Guid.Empty || purchaseLineIds.Any(lineId => lineId == Guid.Empty))
        {
            throw Unavailable();
        }

        var companyId = CompanyId();
        var session = await EnlistAsync(connection, dbTransaction, cancellationToken);
        await using (session)
        {
            var links = new List<PurchaseFutureAllocationLinkV1>();
            try
            {
                foreach (var lineId in purchaseLineIds.Distinct().Order())
                {
                    foreach (var view in await session.ListActiveByPurchaseLineAsync(lineId, cancellationToken))
                    {
                        links.Add(Link(view, companyId, purchaseOrderId, lineId));
                    }
                }
            }
            catch (PurchaseFutureAllocationLinkException)
            {
                throw;
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
            {
                throw NonTerminal(exception);
            }

            return links;
        }
    }

    public async Task<PurchaseFutureAllocationLinkV1> ReleaseAsync(IDbTransaction transaction, PurchaseFutureAllocationLinkReleaseV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var (connection, dbTransaction) = Bound(transaction);
        if (request.PurchaseOrderId == Guid.Empty || request.AllocationId == Guid.Empty || request.ExpectedAllocationVersion < 1
            || string.IsNullOrWhiteSpace(request.ReasonText) || request.Envelope is null || !IsPurchasingReason(request.ReasonCode))
        {
            throw Unavailable();
        }

        var companyId = CompanyId();
        var envelope = request.Envelope;
        var metadata = new InventoryCommandMetadata(envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
            envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent);
        var session = await EnlistAsync(connection, dbTransaction, cancellationToken);
        await using (session)
        {
            InventoryFutureAllocationViewV1 released;
            try
            {
                released = await session.ReleaseAsync(new InventoryFutureAllocationReleaseRequestV1(request.AllocationId,
                        request.ExpectedAllocationVersion, QuantityText: null, request.ReasonCode, request.ReasonText,
                        InventoryFutureAllocationOriginsV1.Purchasing),
                    metadata, cancellationToken);
            }
            catch (InventoryAllocationException exception) when (Terminal(exception.Code) is { } code)
            {
                throw new PurchaseFutureAllocationLinkException(ConflictStatus, code, terminal: true, exception);
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
            {
                throw NonTerminal(exception);
            }

            if (released is null || released.AllocationId != request.AllocationId)
            {
                throw Unavailable();
            }

            return Link(released, companyId, request.PurchaseOrderId, released.PurchaseOrderLineId);
        }
    }

    private Guid CompanyId() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty ? companyContext.CompanyId : throw Unavailable();

    private static (DbConnection Connection, DbTransaction Transaction) Bound(IDbTransaction transaction) =>
        transaction is DbTransaction { Connection: { } connection } dbTransaction ? (connection, dbTransaction) : throw Unavailable();

    private async Task<IInventoryFutureAllocationReleaseSessionV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            return await releases.EnlistAsync(connection, transaction, cancellationToken);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            throw NonTerminal(exception);
        }
    }

    /// <summary>The owner's view, checked against the company, the order and the line before Purchasing shows or releases it.</summary>
    private static PurchaseFutureAllocationLinkV1 Link(InventoryFutureAllocationViewV1? view, Guid companyId, Guid purchaseOrderId,
        Guid purchaseLineId)
    {
        if (view is null || view.AllocationId == Guid.Empty || view.CompanyId != companyId || view.PurchaseOrderId != purchaseOrderId
            || view.PurchaseOrderLineId != purchaseLineId || purchaseLineId == Guid.Empty
            || !long.TryParse(view.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            throw Unavailable();
        }

        return new PurchaseFutureAllocationLinkV1(view.AllocationId, version, view.PurchaseOrderId, view.PurchaseOrderLineId,
            view.SalesOrderId, view.SalesOrderLineId, view.UnitId, view.AllocatedQuantity, view.RemainingQuantity, view.CreatedAtUtc);
    }

    private static bool IsPurchasingReason(string? code) =>
        code is PurchaseFutureAllocationLinkReasonsV1.LinkDropped
            or PurchaseFutureAllocationLinkReasonsV1.SourceCancelled
            or PurchaseFutureAllocationLinkReasonsV1.LineClosed;

    /// <summary>The owner's refusals that are the Purchasing key's recorded answer, in Purchasing's own problem codes.</summary>
    private static string? Terminal(string code) => code switch
    {
        InventoryFutureAllocationCodesV1.VersionConflict => PurchasingProblemCodesV1.FutureAllocationVersionConflict,
        InventoryFutureAllocationCodesV1.StateConflict or InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid
            => PurchasingProblemCodesV1.FutureAllocationState,
        _ => null,
    };

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>A busy line or a concurrent change at the owner is retried as Purchasing's own 409; any other failure is 503.</summary>
    private static PurchaseFutureAllocationLinkException NonTerminal(Exception exception) => exception switch
    {
        PurchaseLineBusyException busy => new(ConflictStatus, PurchasingProblemCodesV1.LineBusy, terminal: false, busy),
        InventoryAllocationException { Code: InventoryFutureAllocationCodesV1.ConcurrentChange } owner
            => new(ConflictStatus, PurchasingProblemCodesV1.ConcurrentChange, terminal: false, owner),
        _ => Unavailable(exception),
    };

    private static PurchaseFutureAllocationLinkException Unavailable(Exception? inner = null) =>
        new(UnavailableStatus, InventoryFutureAllocationCodesV1.ServiceUnavailable, terminal: false, inner);
}
