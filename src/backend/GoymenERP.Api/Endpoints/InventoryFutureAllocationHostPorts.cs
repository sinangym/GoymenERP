using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

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
