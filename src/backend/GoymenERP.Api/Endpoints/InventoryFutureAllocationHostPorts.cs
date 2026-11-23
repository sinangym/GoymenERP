using System.Data;
using System.Data.Common;
using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-017 STOK §3.2 step 0 (F017-b): the SQL Server answer to <see cref="ISalesOrderLineSerializationLockV1"/>, the mirror of the
/// purchase line lock. One fixed statement — <c>sp_getapplock</c> with <c>@LockMode = 'Exclusive'</c> and
/// <c>@LockOwner = 'Transaction'</c> on the bound transaction — over <see cref="SalesOrderLineSerializationLockContractV1.ResourceName"/>;
/// the resource and the wait are the only parameters, never SQL text. 0 or 1 continues; a negative answer is refused with
/// <see cref="SalesOrderLineBusyException"/> and the caller writes nothing. The lock is released with the transaction. It lives in
/// the host because neither Inventory nor Sales references the other; Sales may move it into its own module without a contract
/// change (the resource name is pinned by a contract test).
/// </summary>
public sealed class SalesOrderLineSerializationLock : ISalesOrderLineSerializationLockV1
{
    /// <summary>The command waits a little longer than the lock itself so the lock's own timeout (−1) answers first, not the driver.</summary>
    private static readonly TimeSpan CommandTimeoutMargin = TimeSpan.FromSeconds(5);

    private const string AcquireStatement =
        "DECLARE @result int; "
        + "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', "
        + "@LockTimeout = @timeout; "
        + "SELECT @result;";

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly Guid _companyId;

    private SalesOrderLineSerializationLock(DbConnection connection, DbTransaction transaction, Guid companyId)
    {
        _connection = connection;
        _transaction = transaction;
        _companyId = companyId;
    }

    /// <summary>Binds to the caller's open connection, its own transaction and the server-resolved company; refuses otherwise.</summary>
    public static SalesOrderLineSerializationLock Bind(DbConnection connection, DbTransaction transaction, Guid companyId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Satış satırı kilidi yalnız açık bağlantıda alınır.");
        }

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Satış satırı kilidi yalnız bağlantının kendi işleminde alınır.");
        }

        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("Satış satırı kilidi yalnız çözümlenmiş şirket bağlamında alınır.");
        }

        return new SalesOrderLineSerializationLock(connection, transaction, companyId);
    }

    public async Task AcquireAsync(Guid lineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = SalesOrderLineSerializationLockContractV1.ResourceName(_companyId, lineId);
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = AcquireStatement;
        command.CommandTimeout =
            (int)Math.Ceiling((SalesOrderLineSerializationLockContractV1.LockTimeout + CommandTimeoutMargin).TotalSeconds);

        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.DbType = DbType.String;
        resourceParameter.Size = resource.Length;
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.DbType = DbType.Int32;
        timeoutParameter.Value = (int)SalesOrderLineSerializationLockContractV1.LockTimeout.TotalMilliseconds;
        command.Parameters.Add(timeoutParameter);

        var answer = await command.ExecuteScalarAsync(cancellationToken);
        if (answer is not int result)
        {
            // Not a lock answer at all: a technical failure, never read as "granted" and never as "busy".
            throw new InvalidOperationException("Satış satırı kilidi cevabı okunamadı.");
        }

        if (result < 0)
        {
            throw new SalesOrderLineBusyException(lineId, result);
        }
    }
}

/// <summary>
/// The host binder of <see cref="ISalesOrderLineSerializationLockV1"/>: the caller's connection and transaction under the
/// server-resolved company only; without one it refuses before any statement runs. It grants nothing.
/// </summary>
public sealed class SalesOrderLineSerializationLockHostBinder(ICompanyContext companyContext) : ISalesOrderLineSerializationLockBinderV1
{
    public ISalesOrderLineSerializationLockV1 Bind(DbConnection connection, DbTransaction transaction)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Satış satırı kilidi yalnız çözümlenmiş şirket bağlamında alınır.");
        }

        return SalesOrderLineSerializationLock.Bind(connection, transaction, companyContext.CompanyId);
    }
}

/// <summary>
/// F-017 STOK §3.2 step 1 (F017-b): the host's adapter of Purchasing's <see cref="IPurchaseFutureSupplyReadPortV1"/> to the
/// Inventory-declared <see cref="IInventoryFutureSupplySourceV1"/> (Inventory never references Purchasing). It translates the answer
/// only and grants nothing. Fail-closed: an answer of another company, another order or line, a missing line or an exception is
/// <see cref="InventoryFutureSupplySourceOutcomeV1.Unavailable"/>, never "allocatable".
/// </summary>
public sealed class InventoryFutureSupplyHostSource(IPurchaseFutureSupplyReadPortV1 purchasing, ICompanyContext companyContext)
    : IInventoryFutureSupplySourceV1
{
    private static readonly InventoryFutureSupplySourceAnswerV1 Unavailable =
        new(InventoryFutureSupplySourceOutcomeV1.Unavailable, null);

    public async Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return Unavailable;
        }

        try
        {
            var answer = await purchasing.GetAllocatableLineAsync(purchaseOrderId, purchaseOrderLineId, cancellationToken);
            switch (answer?.Outcome)
            {
                case PurchaseFutureSupplyOutcomeV1.NotFound:
                    return new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.NotFound, null);
                case PurchaseFutureSupplyOutcomeV1.NotAllocatable:
                    return new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.NotAllocatable, null);
                case PurchaseFutureSupplyOutcomeV1.Allocatable:
                    break;
                default:
                    return Unavailable;
            }

            if (answer.Line is not { } line || line.CompanyId != companyContext.CompanyId || line.OrderId != purchaseOrderId
                || line.LineId != purchaseOrderLineId || line.ProductId == Guid.Empty || string.IsNullOrEmpty(line.ProductVersion)
                || line.UnitId == Guid.Empty || string.IsNullOrEmpty(line.AllocatableText))
            {
                return Unavailable;
            }

            return new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable,
                new InventoryFutureSupplyLineV1(line.OrderId, line.LineId, line.ProductId, line.ProductVersion, line.UnitId,
                    line.AllocatableText, line.ObservedOrderVersion));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }
}

/// <summary>
/// X1 (KLN-D28 (9), SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §X1): the host's answer to
/// <see cref="ISalesOrderFutureAllocationReleaseParticipantV1"/>. Inside the Sales cancellation's own transaction (the same
/// connection) it takes every sales line lock of the order (ascending), lists each line's ACTIVE future supply allocations
/// through Inventory's release session, takes every distinct purchase line lock (ascending; F-017 §3.2 order), lists them AGAIN
/// under every lock (a purchase-side release may have ended one in between), then releases each allocation whole with the version
/// it read under the locks, reason <see cref="InventoryFutureAllocationReleaseReasonsV1.SalesLineCancelled"/>
/// and origin <see cref="InventoryFutureAllocationOriginsV1.Sales"/>, under the Sales command's own envelope (actor, permission,
/// correlation, causation, Idempotency-Key — Inventory's audit and outbox carry them). Inventory writes only its own tables;
/// Sales writes only its own. Nothing is committed here: the Sales store commits or rolls back both.
/// <para>
/// Refusals: the owner's version conflict, state conflict and release-selection refusal are TERMINAL (the Sales key records
/// them); evaluation goes on in the fixed order and the FIRST terminal refusal is answered. A busy sales or purchase line, the
/// owner's concurrent change or any other owner failure is NOT terminal and stops at once (the retry asks again). The owner's
/// code is carried unchanged. It grants nothing and chooses no policy: it releases exactly what is active at that moment.
/// </para>
/// </summary>
public sealed class SalesFutureAllocationReleaseHostParticipant(
    ISalesOrderLineSerializationLockBinderV1 salesLineLocks,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    IInventoryFutureAllocationReleasePortV1 releases) : ISalesOrderFutureAllocationReleaseParticipantV1
{
    private const int ConflictStatus = 409;
    private const int UnavailableStatus = 503;

    public async Task ReleaseForCancelledOrderAsync(IDbTransaction transaction, SalesOrderFutureAllocationReleaseRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (transaction is not DbTransaction { Connection: { } connection } dbTransaction
            || request.LineIds is null || request.LineIds.Count == 0 || request.LineIds.Any(lineId => lineId == Guid.Empty)
            || string.IsNullOrWhiteSpace(request.ReasonText) || request.Envelope is null)
        {
            throw Unavailable();
        }

        var lineIds = request.LineIds.Distinct().Order().ToArray();

        // F-017 §3.2: every sales line of the order first (ascending) — the lock a future allocation's own create takes.
        try
        {
            var salesLocks = salesLineLocks.Bind(connection, dbTransaction);
            foreach (var lineId in lineIds)
            {
                await salesLocks.AcquireAsync(lineId, cancellationToken);
            }
        }
        catch (SalesOrderLineBusyException exception)
        {
            throw new SalesFutureAllocationReleaseException(ConflictStatus, exception.Code, terminal: false, exception);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            throw Unavailable(exception);
        }

        IInventoryFutureAllocationReleaseSessionV1 session;
        try
        {
            session = await releases.EnlistAsync(connection, dbTransaction, cancellationToken);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            throw NonTerminal(exception);
        }

        await using (session)
        {
            // A first read only finds the purchase lines to lock: the sales line locks keep any new allocation of these lines out,
            // but a purchase-side release (it takes the purchase line lock only) may still change or end one of them until the
            // purchase line locks are held. The views released below are therefore read AGAIN under every lock (K3-A P3).
            var candidates = await ListActiveAsync(session, lineIds, request.OrderId, cancellationToken);
            if (candidates.Count == 0)
            {
                return;
            }

            // Then every distinct purchase line (ascending), before the first release.
            var lockedPurchaseLines = candidates.Select(view => view.PurchaseOrderLineId).Distinct().Order().ToArray();
            try
            {
                var purchaseLocks = purchaseLineLocks.Bind(connection, dbTransaction);
                foreach (var purchaseLineId in lockedPurchaseLines)
                {
                    await purchaseLocks.AcquireAsync(purchaseLineId, cancellationToken);
                }
            }
            catch (PurchaseLineBusyException exception)
            {
                throw new SalesFutureAllocationReleaseException(ConflictStatus, exception.Code, terminal: false, exception);
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
            {
                throw Unavailable(exception);
            }

            // The answer under every lock is the one released: what a concurrent purchase-side release already ended is not
            // released again (no stale version reaches the owner, so no terminal 409 is recorded for a race). A view on a purchase
            // line that is not locked cannot appear while the sales line locks are held; if it does, the read is not trusted and
            // the retry asks again (not recorded).
            var active = await ListActiveAsync(session, lineIds, request.OrderId, cancellationToken);
            if (active.Any(view => Array.BinarySearch(lockedPurchaseLines, view.PurchaseOrderLineId) < 0))
            {
                throw new SalesFutureAllocationReleaseException(ConflictStatus, InventoryFutureAllocationCodesV1.ConcurrentChange,
                    terminal: false);
            }

            if (active.Count == 0)
            {
                return;
            }

            var envelope = request.Envelope;
            var metadata = new InventoryCommandMetadata(envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes,
                envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel,
                envelope.ClientIpAddress, envelope.UserAgent);
            SalesFutureAllocationReleaseException? firstTerminal = null;
            foreach (var view in active.OrderBy(view => view.SalesOrderLineId).ThenBy(view => view.PurchaseOrderLineId)
                         .ThenBy(view => view.AllocationId))
            {
                if (!long.TryParse(view.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
                    || expectedVersion < 1)
                {
                    throw Unavailable();
                }

                try
                {
                    await session.ReleaseAsync(new InventoryFutureAllocationReleaseRequestV1(view.AllocationId, expectedVersion,
                            QuantityText: null, InventoryFutureAllocationReleaseReasonsV1.SalesLineCancelled, request.ReasonText,
                            InventoryFutureAllocationOriginsV1.Sales),
                        metadata, cancellationToken);
                }
                catch (InventoryAllocationException exception) when (IsTerminal(exception.Code))
                {
                    firstTerminal ??= new SalesFutureAllocationReleaseException(exception.Status, exception.Code, terminal: true,
                        exception);
                }
                catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
                {
                    throw NonTerminal(exception);
                }
            }

            if (firstTerminal is not null)
            {
                throw firstTerminal;
            }
        }
    }

    /// <summary>Every ACTIVE allocation of the order's lines, checked against the order and the line before it is trusted.</summary>
    private static async Task<List<InventoryFutureAllocationViewV1>> ListActiveAsync(IInventoryFutureAllocationReleaseSessionV1 session,
        IReadOnlyList<Guid> lineIds, Guid orderId, CancellationToken cancellationToken)
    {
        var active = new List<InventoryFutureAllocationViewV1>();
        try
        {
            foreach (var lineId in lineIds)
            {
                foreach (var view in await session.ListActiveByDemandLineAsync(lineId, cancellationToken))
                {
                    if (view is null || view.SalesOrderLineId != lineId || view.SalesOrderId != orderId
                        || view.PurchaseOrderLineId == Guid.Empty || view.AllocationId == Guid.Empty)
                    {
                        // Another order's or a malformed answer: never released on its word.
                        throw Unavailable();
                    }

                    active.Add(view);
                }
            }
        }
        catch (SalesFutureAllocationReleaseException)
        {
            throw;
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            throw NonTerminal(exception);
        }

        return active;
    }

    /// <summary>The owner's refusals that are the Sales key's recorded answer (X1); every other refusal is retried.</summary>
    private static bool IsTerminal(string code) =>
        code is InventoryFutureAllocationCodesV1.VersionConflict
            or InventoryFutureAllocationCodesV1.StateConflict
            or InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid;

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>The owner's own non-terminal refusal keeps its status and code; any other failure is 503.</summary>
    private static SalesFutureAllocationReleaseException NonTerminal(Exception exception) =>
        exception is InventoryAllocationException owner
            ? new SalesFutureAllocationReleaseException(owner.Status, owner.Code, terminal: false, owner)
            : Unavailable(exception);

    private static SalesFutureAllocationReleaseException Unavailable(Exception? inner = null) =>
        new(UnavailableStatus, InventoryFutureAllocationCodesV1.ServiceUnavailable, terminal: false, inner);
}
