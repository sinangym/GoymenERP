using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §4 (F017-b): Inventory's answer to <see cref="IInventoryFutureAllocationReadPortV1"/> for Purchasing and Sales. It
/// reads only under the server-resolved company (the module query filter), untracked, and writes nothing. The ledger figure is
/// trusted only when it equals Σ remaining of the line's active rows; any disagreement or read failure is
/// <see cref="InventoryFutureAllocationReadOutcomeV1.Unreadable"/> (fail-closed), never an assumed zero. No authority: the caller
/// fences its own permission, and a decision that must hold re-reads under the purchase line lock in its own transaction.
/// </summary>
internal sealed class InventoryFutureAllocationReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryFutureAllocationReadPortV1
{
    private static readonly InventoryFutureAllocationSumV1 UnreadableSum =
        new(InventoryFutureAllocationReadOutcomeV1.Unreadable, null, null);

    private static readonly InventoryFutureAllocationSumV1 NoneSum =
        new(InventoryFutureAllocationReadOutcomeV1.None, null, null);

    public async Task<InventoryFutureAllocationSumV1> GetAllocatedOnPurchaseLineAsync(Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || purchaseOrderLineId == Guid.Empty)
        {
            return UnreadableSum;
        }

        try
        {
            var ledger = await dbContext.FutureSupplyLines.AsNoTracking()
                .Where(row => row.PurchaseOrderLineId == purchaseOrderLineId)
                .Select(row => new { row.ActiveAllocated, row.PurchaseUnitId })
                .SingleOrDefaultAsync(cancellationToken);
            var activeRemaining = await dbContext.FutureSupplyAllocations.AsNoTracking()
                .Where(row => row.PurchaseOrderLineId == purchaseOrderLineId
                    && row.Status == (int)FutureSupplyAllocationStatus.Active)
                .SumAsync(row => (decimal?)row.RemainingQuantity, cancellationToken) ?? 0m;
            if (ledger is null)
            {
                return activeRemaining == 0m ? NoneSum : UnreadableSum;
            }

            if (ledger.ActiveAllocated != activeRemaining || ledger.PurchaseUnitId == Guid.Empty)
            {
                return UnreadableSum;
            }

            return ledger.ActiveAllocated == 0m
                ? NoneSum
                : new InventoryFutureAllocationSumV1(InventoryFutureAllocationReadOutcomeV1.Present,
                    FutureSupplyQuantity.Of(ledger.ActiveAllocated).ToString(), ledger.PurchaseUnitId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return UnreadableSum;
        }
    }

    public async Task<InventoryFutureAllocationPresenceV1> GetConsumedPresenceOnPurchaseLineAsync(Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || purchaseOrderLineId == Guid.Empty)
        {
            return new InventoryFutureAllocationPresenceV1(InventoryFutureAllocationReadOutcomeV1.Unreadable);
        }

        try
        {
            var consumed = await dbContext.FutureSupplyAllocations.AsNoTracking()
                .AnyAsync(row => row.PurchaseOrderLineId == purchaseOrderLineId
                    && (row.Status == (int)FutureSupplyAllocationStatus.Consumed
                        || row.AllocatedQuantity - row.RemainingQuantity - row.ReleasedQuantity > 0m),
                    cancellationToken);
            return new InventoryFutureAllocationPresenceV1(consumed
                ? InventoryFutureAllocationReadOutcomeV1.Present
                : InventoryFutureAllocationReadOutcomeV1.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new InventoryFutureAllocationPresenceV1(InventoryFutureAllocationReadOutcomeV1.Unreadable);
        }
    }
}

/// <summary>
/// F-017 STOK §3.3/§4 (F017-b): Inventory's answer to <see cref="IInventoryFutureAllocationReleasePortV1"/>. The session is an
/// InventoryDbContext on the caller's open connection and transaction for the server-resolved company, with Inventory's own audit
/// interceptor; the caller owns commit and rollback and Inventory never closes the connection. Inventory still writes only its own
/// tables. A release asked through the port records no Inventory idempotency row: the caller's command owns the attempt.
/// </summary>
internal sealed class InventoryFutureAllocationReleasePort(
    ICompanyContext companyContext,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    TimeProvider timeProvider,
    IInventoryFutureUnitConverterV1? unitConverter = null,
    IInventoryFutureSupplyScheduleSourceV1? schedules = null) : IInventoryFutureAllocationReleasePortV1
{
    public async Task<IInventoryFutureAllocationReleaseSessionV1> EnlistAsync(DbConnection connection, DbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Gelecek tedarik bırakma çözümlenmiş şirket ve sahibin açık işlemini ister.");
        }

        var companyId = companyContext.CompanyId;
        var company = new CompanyContext();
        company.Set(companyId);
        var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
            .Options;
        var context = new InventoryDbContext(options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new InventoryFutureAllocationReleaseSession(context, audit, connection, transaction, companyId, companyContext,
                purchaseLineLocks, timeProvider, unitConverter ?? new InventoryIdentityFutureUnitConverterV1(),
                schedules ?? new InventoryUnavailableFutureSupplyScheduleSourceV1());
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}

internal sealed partial class InventoryFutureAllocationReleaseSession(
    InventoryDbContext context,
    AuditEventCollector auditEvents,
    DbConnection connection,
    DbTransaction transaction,
    Guid companyId,
    ICompanyContext companyContext,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    TimeProvider timeProvider,
    IInventoryFutureUnitConverterV1 unitConverter,
    IInventoryFutureSupplyScheduleSourceV1 schedules) : IInventoryFutureAllocationReleaseSessionV1
{
    private bool disposed;

    public async Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (purchaseOrderLineId == Guid.Empty)
        {
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }

        var rows = await context.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.PurchaseOrderLineId == purchaseOrderLineId && row.Status == (int)FutureSupplyAllocationStatus.Active)
            .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(Engine.View)];
    }

    public async Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid salesOrderLineId,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (salesOrderLineId == Guid.Empty)
        {
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }

        var rows = await context.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.DemandKind == InventoryFutureAllocationKindsV1.SalesOrderLine
                && row.DemandLineId == salesOrderLineId && row.Status == (int)FutureSupplyAllocationStatus.Active)
            .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(Engine.View)];
    }

    public async Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Engine.RequireMetadata(metadata);
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        if (request.AllocationId == Guid.Empty || request.ExpectedAllocationVersion <= 0
            || !InventoryFutureAllocationOriginsV1.IsKnown(request.Origin))
        {
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }

        var (quantity, reasonCode, reasonText) = Engine.ParseRelease(request.QuantityText, request.ReasonCode, request.ReasonText);
        var purchaseLineId = await context.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.Id == request.AllocationId)
            .Select(row => (Guid?)row.PurchaseOrderLineId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference);

        // §3.3: the purchase line lock in the caller's transaction (re-entrant when the caller already holds it).
        try
        {
            await purchaseLineLocks.Bind(connection, transaction).AcquireAsync(purchaseLineId, cancellationToken);
        }
        catch (PurchaseLineBusyException exception)
        {
            throw Engine.Error(409, exception.Code, exception);
        }

        var row = await context.FutureSupplyAllocations.SingleOrDefaultAsync(item => item.Id == request.AllocationId,
                cancellationToken)
            ?? throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference);
        if (row.PurchaseOrderLineId != purchaseLineId)
        {
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }

        var now = timeProvider.GetUtcNow();
        var step = await Engine.ReleaseTrackedAsync(context, row, request.ExpectedAllocationVersion, quantity, reasonCode,
            request.Origin, reasonText, now, cancellationToken);
        Engine.Audit(auditEvents, metadata, companyId, row.Id, InventoryFutureAllocationReleasedOutboxV1.EventType,
            Engine.ReleasedOperation, step.VersionBefore, row.Version, JsonSerializer.Serialize(step.Before, Engine.Json),
            JsonSerializer.Serialize(step.After, Engine.Json), reasonText);
        Engine.AddReleasedOutbox(context, row, step, metadata, now);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // The caller's transaction is no longer usable for this release; the caller rolls back.
            context.ChangeTracker.Clear();
            throw Engine.Error(409, InventoryFutureAllocationCodesV1.ConcurrentChange, exception);
        }

        return step.After;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await context.DisposeAsync();
    }

    /// <summary>The session works only while the caller's transaction is live and the request's company is still the session's.</summary>
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } enlisted
            || !ReferenceEquals(enlisted.GetDbTransaction(), transaction)
            || !companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            throw new InvalidOperationException("Gelecek tedarik bırakma oturumu artık sahibin işleminde değil.");
        }
    }
}
