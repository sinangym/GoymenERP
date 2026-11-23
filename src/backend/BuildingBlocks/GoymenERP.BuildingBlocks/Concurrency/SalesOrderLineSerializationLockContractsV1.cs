using System.Data.Common;
using System.Globalization;

namespace GoymenERP.BuildingBlocks.Concurrency;

/// <summary>
/// F-017 STOK §3.2 step 0: the cross-module serialization point of one sales order line (the demand side of a future supply
/// allocation). A path that reads a sales line's open quantity and its existing physical and future allocations and then writes
/// against them takes this lock in the SAME database transaction before its reads. Lock order is binding: the sales line lock is
/// taken FIRST and the purchase line lock (<see cref="IPurchaseLineSerializationLockV1"/>) second; the two are never interleaved
/// in the opposite order. The lock is transaction-owned (released by commit or rollback) and re-entrant for the same transaction.
/// The resource name is settled by F017-b and pinned by a contract test.
/// </summary>
public interface ISalesOrderLineSerializationLockV1
{
    /// <summary>
    /// Takes the exclusive lock of one sales order line in the bound transaction. Throws <see cref="SalesOrderLineBusyException"/>
    /// when the lock cannot be granted within <see cref="SalesOrderLineSerializationLockContractV1.LockTimeout"/> (the caller
    /// writes nothing and answers 409 <see cref="SalesOrderLineSerializationLockContractV1.BusyCode"/>); a database failure
    /// propagates as a technical failure and is never read as "busy" or as "granted".
    /// </summary>
    Task AcquireAsync(Guid lineId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The host's way to bind <see cref="ISalesOrderLineSerializationLockV1"/> to the caller's own open connection and transaction.
/// The company is never a parameter: the binder takes it from the server-resolved company context and refuses without one.
/// </summary>
public interface ISalesOrderLineSerializationLockBinderV1
{
    ISalesOrderLineSerializationLockV1 Bind(DbConnection connection, DbTransaction transaction);
}

/// <summary>The fixed parts of the sales order line lock contract; the resource name is pinned by a contract test.</summary>
public static class SalesOrderLineSerializationLockContractV1
{
    /// <summary>The application lock resource prefix (F-017 STOK §3.2 step 0).</summary>
    public const string ResourcePrefix = "goymen:sales-order-line:";

    /// <summary>The 409 problem code a refused (timed out) lock answers with; no write happens (F-017 STOK §7).</summary>
    public const string BusyCode = "sales_order_line_busy";

    /// <summary>
    /// How long one acquisition waits before it is refused. Technical, reversible value mirrored from the purchase line lock
    /// (F-017 §3.4 "kısa, öneri 5 sn"); not a business tolerance.
    /// </summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <c>goymen:sales-order-line:{companyId:N}:{lineId:N}</c> — 24 + 32 + 1 + 32 = 89 characters, inside the 255-character
    /// application lock resource limit. Both identities are required.
    /// </summary>
    public static string ResourceName(Guid companyId, Guid lineId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Satış satırı kilidi yalnız çözümlenmiş şirket için alınır.", nameof(companyId));
        }

        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("Satış satırı kilidi için satış sipariş satırı kimliği gereklidir.", nameof(lineId));
        }

        return string.Create(CultureInfo.InvariantCulture, $"{ResourcePrefix}{companyId:N}:{lineId:N}");
    }
}

/// <summary>The sales order line lock was not granted in time; the caller writes nothing and answers 409 <see cref="SalesOrderLineSerializationLockContractV1.BusyCode"/>.</summary>
public sealed class SalesOrderLineBusyException : Exception
{
    public SalesOrderLineBusyException(Guid lineId, int lockResult)
        : base("Satış sipariş satırı başka bir işlem tarafından kilitli.")
    {
        LineId = lineId;
        LockResult = lockResult;
    }

    public Guid LineId { get; }

    /// <summary>The application lock's negative answer (−1 timeout, −2 cancelled, −3 deadlock victim, −999 parameter or call error).</summary>
    public int LockResult { get; }

    public string Code => SalesOrderLineSerializationLockContractV1.BusyCode;
}
