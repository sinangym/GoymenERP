using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-S8 / KLN-D28/30/31/32/105. Sales computes the newly effective open demand. This adapter takes ALL Sales line locks,
/// then ALL purchase line locks in ascending order before asking Inventory to reduce any line. Inventory's writes enlist in
/// the exact Sales transaction; only the Sales caller commits or rolls back. No extra Inventory permission or idempotency
/// record is introduced. An earlier terminal refusal cannot hide a later unreadable source or concurrent change.
/// </summary>
public sealed class SalesFutureDemandReductionHostParticipant(
    ISalesOrderLineSerializationLockBinderV1 salesLineLocks,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    IInventoryFutureAllocationReleasePortV1 releases,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority) : ISalesOrderFutureDemandReductionParticipantV1
{
    public async Task ReduceForAcceptedRevisionAsync(IDbTransaction transaction,
        SalesOrderFutureDemandReductionRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (transaction is not DbTransaction { Connection: { State: ConnectionState.Open } connection } dbTransaction
            || request.OrderId == Guid.Empty || request.Revision < 1 || request.Lines is null || request.Lines.Count == 0
            || request.Lines.Any(line => line is null || line.OrderLineId == Guid.Empty || line.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(line.ProductVersion) || line.DemandUnitId == Guid.Empty
                || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(line.NewOpenQuantityText, out _))
            || request.Lines.Select(line => line.OrderLineId).Distinct().Count() != request.Lines.Count
            || string.IsNullOrWhiteSpace(request.ReasonText) || request.Envelope is null)
            throw Unavailable();

        var companyId = companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty;
        var envelope = request.Envelope;
        if (envelope.PermissionCode is not (SalesPermissionManifestV1.OrdersApprove
            or SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord)) throw Unavailable();
        await RequireStillAsync(companyId, envelope, cancellationToken);
        var lines = request.Lines.OrderBy(line => line.OrderLineId).ToArray();

        try
        {
            var salesLocks = salesLineLocks.Bind(connection, dbTransaction);
            foreach (var line in lines) await salesLocks.AcquireAsync(line.OrderLineId, cancellationToken);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            throw NonTerminal(exception);
        }
        await RequireStillAsync(companyId, envelope, cancellationToken);

        IInventoryFutureAllocationReleaseSessionV1 session;
        try { session = await releases.EnlistAsync(connection, dbTransaction, cancellationToken); }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken)) { throw NonTerminal(exception); }
        await using (session)
        {
            var purchaseIds = new HashSet<Guid>();
            var allocationIds = new HashSet<Guid>();
            try
            {
                foreach (var line in lines)
                    foreach (var view in await ReadActiveAsync(line))
                    {
                        if (!allocationIds.Add(view.AllocationId)) throw Unavailable();
                        purchaseIds.Add(view.PurchaseOrderLineId);
                    }
                // The union across ALL demand lines is locked before the first reduction. Per-line locks in Inventory
                // are consequently reentrant; a second demand line can never acquire a lower purchase id after a higher one.
                var purchaseLocks = purchaseLineLocks.Bind(connection, dbTransaction);
                foreach (var purchaseId in purchaseIds.Order()) await purchaseLocks.AcquireAsync(purchaseId, cancellationToken);
                // Creates share the Sales lock. If an owner answer nevertheless grows beyond the prelocked set, retry
                // rather than acquiring another lock out of the canonical order.
                foreach (var line in lines)
                    foreach (var view in await ReadActiveAsync(line))
                        if (!purchaseIds.Contains(view.PurchaseOrderLineId)) throw Unavailable();
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken)) { throw NonTerminal(exception); }

            var metadata = new InventoryCommandMetadata(envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
                envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel,
                envelope.ClientIpAddress, envelope.UserAgent);
            SalesFutureAllocationReleaseException? firstTerminal = null;
            foreach (var line in lines)
            {
                await RequireStillAsync(companyId, envelope, cancellationToken);
                try
                {
                    // Even an empty future set can contain a physical allocation. Let the owner measure that case.
                    // Null means its decision-locked default selection, whereas [] explicitly selects no release.
                    await session.ReduceDemandAsync(new(request.OrderId, line.OrderLineId, line.ProductId,
                        line.ProductVersion, line.DemandUnitId, line.NewOpenQuantityText, request.ReasonText, Selections: null),
                        metadata, cancellationToken);
                }
                catch (InventoryAllocationException exception) when (IsTerminal(exception))
                {
                    firstTerminal ??= new(exception.Status, exception.Code, terminal: true, exception);
                }
                catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken)) { throw NonTerminal(exception); }
            }
            await RequireStillAsync(companyId, envelope, cancellationToken);
            if (firstTerminal is not null) throw firstTerminal;

            async Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ReadActiveAsync(SalesOrderFutureDemandReductionLineV1 line)
            {
                var views = await session.ListActiveByDemandLineAsync(line.OrderLineId, cancellationToken);
                if (views is null || views.Any(view => view is null || view.CompanyId != companyId
                    || view.SalesOrderId != request.OrderId || view.SalesOrderLineId != line.OrderLineId
                    || view.ProductId != line.ProductId || view.PurchaseOrderId == Guid.Empty
                    || view.PurchaseOrderLineId == Guid.Empty || view.AllocationId == Guid.Empty
                    || view.Status != nameof(FutureSupplyAllocationStatus.Active)
                    || !InventoryFutureAllocationTextV1.TryVersion(view.Version, out _))) throw Unavailable();
                return views;
            }
        }
    }

    private async Task RequireStillAsync(Guid companyId, SalesDraftWriteEnvelope envelope, CancellationToken token)
    {
        if (companyId == Guid.Empty || !companyContext.IsResolved || companyContext.CompanyId != companyId
            || !currentUser.IsAuthenticated || !currentUser.CanAccessCompany(companyId)
            || !string.Equals(currentUser.UserId, envelope.ActorId, StringComparison.Ordinal))
            throw new SalesDraftServiceException(403, "forbidden");
        var scope = await authority.RevalidatePermissionScopeAsync(envelope.PermissionCode, companyId, token);
        if (!scope.IsGranted || !scope.IsCompanyWide || !companyContext.IsResolved || companyContext.CompanyId != companyId
            || !currentUser.IsAuthenticated || !currentUser.CanAccessCompany(companyId)
            || !string.Equals(currentUser.UserId, envelope.ActorId, StringComparison.Ordinal))
            throw new SalesDraftServiceException(403, "forbidden");
    }

    private static bool IsTerminal(InventoryAllocationException exception) => (exception.Status, exception.Code) is
        (409, InventoryFutureAllocationCodesV1.VersionConflict or InventoryFutureAllocationCodesV1.StateConflict
            or InventoryFutureAllocationCodesV1.PhysicalReleaseUnavailable)
        or (422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid or InventoryFutureAllocationCodesV1.UnitConversionUnavailable);

    private static bool IsCallerCancellation(Exception exception, CancellationToken token) =>
        exception is OperationCanceledException && token.IsCancellationRequested;

    private static SalesFutureAllocationReleaseException NonTerminal(Exception exception) => exception switch
    {
        SalesOrderLineBusyException busy => new(409, busy.Code, false, busy),
        PurchaseLineBusyException busy => new(409, busy.Code, false, busy),
        InventoryAllocationException { Status: 409 or 503 } owner => new(owner.Status, owner.Code, false, owner),
        _ => Unavailable(exception),
    };

    private static SalesFutureAllocationReleaseException Unavailable(Exception? inner = null) =>
        new(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, false, inner);
}
