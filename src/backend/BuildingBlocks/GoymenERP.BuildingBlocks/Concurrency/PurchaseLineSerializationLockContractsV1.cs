using System.Data.Common;
using System.Globalization;

namespace GoymenERP.BuildingBlocks.Concurrency;

/// <summary>
/// F-017 §3.4 (ALIS_F017 v1, STOK_F017 §3.2 step 0): the one cross-module serialization point of a purchase GENERAL line. Every
/// path that reads a purchase line's allocatable amount and then writes against it (Inventory future allocation create/release,
/// Purchasing acceptance share, Purchasing order remainder cancel, and the later reduce/close/reverse paths) takes this lock in the
/// SAME database transaction before its read, so "read allocatable → check → write" can never interleave with another module's
/// write to that line. The lock is transaction-owned: it is released by the transaction's commit or rollback and never outlives
/// it. It is re-entrant for the same transaction (the acceptance share and the Inventory conversion in one host transaction do not
/// block each other). Neither module references the other; both reach the lock through this contract and the host binder.
/// </summary>
public interface IPurchaseLineSerializationLockV1
{
    /// <summary>
    /// Takes the exclusive lock of one purchase general line in the bound transaction. Throws <see cref="PurchaseLineBusyException"/>
    /// when the lock cannot be granted within <see cref="PurchaseLineSerializationLockContractV1.LockTimeout"/> (the caller writes
    /// nothing and answers 409 <see cref="PurchaseLineSerializationLockContractV1.BusyCode"/>); a database failure propagates as a
    /// technical failure and is never read as "busy" or as "granted".
    /// </summary>
    Task AcquireAsync(Guid lineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the locks of several purchase general lines in the one canonical order
    /// (<see cref="PurchaseLineSerializationLockContractV1.AcquisitionOrder"/>: distinct, ascending), so two paths that lock the
    /// same lines can never deadlock on each other. The first refusal stops the walk and throws; locks already granted stay with
    /// the transaction and are released by its rollback.
    /// </summary>
    Task AcquireAllAsync(IEnumerable<Guid> lineIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The host's way to bind <see cref="IPurchaseLineSerializationLockV1"/> to the caller's own open connection and transaction. The
/// company is never a parameter: the binder takes it from the server-resolved company context and refuses without one.
/// </summary>
public interface IPurchaseLineSerializationLockBinderV1
{
    IPurchaseLineSerializationLockV1 Bind(DbConnection connection, DbTransaction transaction);
}

/// <summary>The fixed parts of the purchase line lock contract; the resource name is pinned by a contract test on both sides.</summary>
public static class PurchaseLineSerializationLockContractV1
{
    /// <summary>The application lock resource prefix (F-017 §3.4).</summary>
    public const string ResourcePrefix = "goymen:purchase-line:";

    /// <summary>The 409 problem code a refused (timed out) lock answers with; no write happens (F-017 §3.4, STOK_F017 §3.2 step 0).</summary>
    public const string BusyCode = "purchasing_line_busy";

    /// <summary>
    /// How long one acquisition waits before it is refused. Technical, reversible value proposed by F-017 §3.4 ("kısa, öneri
    /// 5 sn"); not a business tolerance.
    /// </summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <c>goymen:purchase-line:{companyId:N}:{lineId:N}</c> — 21 + 32 + 1 + 32 = 86 characters, inside the 255-character
    /// application lock resource limit. Both identities are required.
    /// </summary>
    public static string ResourceName(Guid companyId, Guid lineId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Satır kilidi yalnız çözümlenmiş şirket için alınır.", nameof(companyId));
        }

        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("Satır kilidi için satınalma satırı kimliği gereklidir.", nameof(lineId));
        }

        return string.Create(CultureInfo.InvariantCulture, $"{ResourcePrefix}{companyId:N}:{lineId:N}");
    }

    /// <summary>
    /// The one canonical acquisition order of several purchase line locks: duplicates removed, ascending by <see cref="Guid.CompareTo(Guid)"/>.
    /// Every path uses this helper, so the order is the same everywhere. An empty identity is refused.
    /// </summary>
    public static IReadOnlyList<Guid> AcquisitionOrder(IEnumerable<Guid> lineIds)
    {
        ArgumentNullException.ThrowIfNull(lineIds);
        var ordered = new SortedSet<Guid>();
        foreach (var lineId in lineIds)
        {
            if (lineId == Guid.Empty)
            {
                throw new ArgumentException("Satır kilidi için satınalma satırı kimliği gereklidir.", nameof(lineIds));
            }

            ordered.Add(lineId);
        }

        return [.. ordered];
    }
}

/// <summary>The purchase line lock was not granted in time; the caller writes nothing and answers 409 <see cref="PurchaseLineSerializationLockContractV1.BusyCode"/>.</summary>
public sealed class PurchaseLineBusyException : Exception
{
    public PurchaseLineBusyException(Guid lineId, int lockResult)
        : base("Satınalma satırı başka bir işlem tarafından kilitli.")
    {
        LineId = lineId;
        LockResult = lockResult;
    }

    public Guid LineId { get; }

    /// <summary>The application lock's negative answer (−1 timeout, −2 cancelled, −3 deadlock victim, −999 parameter or call error).</summary>
    public int LockResult { get; }

    public string Code => PurchaseLineSerializationLockContractV1.BusyCode;
}
