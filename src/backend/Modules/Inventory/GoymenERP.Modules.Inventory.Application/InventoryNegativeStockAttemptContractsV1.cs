using GoymenERP.Modules.Inventory.Domain.Balances;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>SAT-D328/336..339: stopped physical exits, never reservations and never stock movements.</summary>
public static class InventoryNegativeStockAttemptApiV1
{
    public const string Path = "/api/v1/inventory/negative-stock-attempts";
    public const string GetOperationId = "GetInventoryNegativeStockAttemptsV1";
    public const string CommandName = "inventory.movement.record.v1";
}

public interface IInventoryNegativeStockAttemptsV1
{
    Task<InventoryNegativeStockAttemptPageV1> SearchAsync(InventoryNegativeStockAttemptQueryV1 query,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryNegativeStockAttemptQueryV1(Guid? ProductId = null, Guid? LocationId = null,
    bool OpenOnly = false, int Limit = 50);
public sealed record InventoryNegativeStockAttemptPageV1(Guid CompanyId,
    IReadOnlyList<InventoryNegativeStockAttemptViewV1> Items, bool WindowFull);
public sealed record InventoryNegativeStockAttemptViewV1(Guid AttemptId, Guid CompanyId, Guid MovementId,
    string CommandName, string ActorId, DateTimeOffset OccurredAtUtc, Guid ProductId, Guid LocationId,
    Guid LabelId, string LabelCode, string MovementKind, string SourceKind, Guid SourceId, Guid? SourceLineId,
    string RequestedKg, string AvailableKg, string MissingKg, InventoryNegativeStockAttemptResolutionV1? Resolution);
public sealed record InventoryNegativeStockAttemptResolutionV1(Guid StockCountId, DateTimeOffset DecidedAtUtc,
    string DecidedBy);

/// <summary>
/// Called only after the enclosing physical transaction has successfully rolled back (or before any transaction
/// was opened). It commits the Inventory attempt and its Audit owner batch in a fresh transaction. It does not
/// mark the physical command as terminal, retry it, or claim that a failed rollback was successful.
/// </summary>
public interface IInventoryNegativeStockAttemptRecorderV1
{
    Task RecordAfterRollbackAsync(Exception refusal, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owner-produced proof attached to the rule exception before any refused movement writes. There is no HTTP
/// write surface for this data; the recorder verifies the company/actor/permission again. The immutable payload
/// hash is of the real proposed movement, not of a caller-authored report.
/// </summary>
public sealed record InventoryNegativeStockAttemptEvidenceV1(Guid MovementId, string MovementPayloadHash,
    StockShortfall Shortfall, string LabelCode, DateTimeOffset RefusedAtUtc, InventoryCommandMetadata Metadata,
    long? ObservedCountBoundaryVersion = null);

public sealed class InventoryNegativeStockAttemptEvidenceException(InventoryNegativeStockAttemptEvidenceV1 evidence,
    Exception innerException) : Exception("inventory_physical_stock_refusal_evidence", innerException)
{
    public InventoryNegativeStockAttemptEvidenceV1 Evidence { get; } = evidence;

    public static InventoryNegativeStockAttemptEvidenceV1? FindEvidence(Exception exception)
    {
        // A bounded chain prevents an untrusted/custom exception from turning this into unbounded work.
        for (var depth = 0; exception is not null && depth < 32; depth++, exception = exception.InnerException!)
            if (exception is InventoryNegativeStockAttemptEvidenceException evidence) return evidence.Evidence;
        return null;
    }
}

public static class InventoryNegativeStockAttemptCodesV1
{
    public const string Unauthenticated = "inventory_negative_stock_attempts_unauthenticated";
    public const string Forbidden = "inventory_negative_stock_attempts_forbidden";
    public const string InvalidQuery = "inventory_negative_stock_attempts_invalid_query";
    public const string Unavailable = "inventory_negative_stock_attempts_unavailable";
}
public sealed class InventoryNegativeStockAttemptException(int statusCode, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
