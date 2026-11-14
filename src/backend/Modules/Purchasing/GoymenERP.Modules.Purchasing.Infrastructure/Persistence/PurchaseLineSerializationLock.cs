using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// F-017 F17-1 (ALIS_F017 v1 §3.4): the SQL Server answer to <see cref="IPurchaseLineSerializationLockV1"/>. One fixed statement —
/// <c>sp_getapplock</c> with <c>@LockMode = 'Exclusive'</c> and <c>@LockOwner = 'Transaction'</c> on the bound transaction —
/// over the resource <see cref="PurchaseLineSerializationLockContractV1.ResourceName"/>; the resource and the wait are the only
/// parameters, never SQL text. A return code of 0 (granted) or 1 (granted after waiting) continues; any negative code is refused
/// with <see cref="PurchaseLineBusyException"/> and the caller writes nothing (fail-closed: a negative answer is never "granted").
/// The lock is released with the transaction; there is no release call and no session-owned lock.
/// Purchasing owns the purchase line identity, so the statement lives here; Inventory reaches it only through the host binder and
/// the BuildingBlocks contract, never by referencing this module.
/// </summary>
public sealed class PurchaseLineSerializationLock : IPurchaseLineSerializationLockV1
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

    private PurchaseLineSerializationLock(DbConnection connection, DbTransaction transaction, Guid companyId)
    {
        _connection = connection;
        _transaction = transaction;
        _companyId = companyId;
    }

    /// <summary>
    /// Binds the lock to the caller's open connection, its transaction and the server-resolved company. Refuses a closed connection,
    /// a transaction of another connection and an empty company before any statement runs.
    /// </summary>
    public static PurchaseLineSerializationLock Bind(DbConnection connection, DbTransaction transaction, Guid companyId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Satır kilidi yalnız açık bağlantıda alınır.");
        }

        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Satır kilidi yalnız bağlantının kendi işleminde alınır.");
        }

        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("Satır kilidi yalnız çözümlenmiş şirket bağlamında alınır.");
        }

        return new PurchaseLineSerializationLock(connection, transaction, companyId);
    }

    public async Task AcquireAsync(Guid lineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = PurchaseLineSerializationLockContractV1.ResourceName(_companyId, lineId);
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandType = CommandType.Text;
        command.CommandText = AcquireStatement;
        command.CommandTimeout = (int)Math.Ceiling((PurchaseLineSerializationLockContractV1.LockTimeout + CommandTimeoutMargin).TotalSeconds);

        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.DbType = DbType.String;
        resourceParameter.Size = resource.Length;
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.DbType = DbType.Int32;
        timeoutParameter.Value = (int)PurchaseLineSerializationLockContractV1.LockTimeout.TotalMilliseconds;
        command.Parameters.Add(timeoutParameter);

        var answer = await command.ExecuteScalarAsync(cancellationToken);
        if (answer is not int result)
        {
            // Not a lock answer at all: a technical failure, never read as "granted" and never as "busy".
            throw new InvalidOperationException("Satır kilidi cevabı okunamadı.");
        }

        if (result < 0)
        {
            throw new PurchaseLineBusyException(lineId, result);
        }
    }

    public async Task AcquireAllAsync(IEnumerable<Guid> lineIds, CancellationToken cancellationToken = default)
    {
        foreach (var lineId in PurchaseLineSerializationLockContractV1.AcquisitionOrder(lineIds))
        {
            await AcquireAsync(lineId, cancellationToken);
        }
    }
}
