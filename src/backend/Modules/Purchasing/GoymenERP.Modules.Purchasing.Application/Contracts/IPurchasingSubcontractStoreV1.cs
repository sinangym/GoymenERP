namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchasingSubcontractStoreResultV1(StoredPurchasingSubcontractV1 Order,
    PurchasingWriteOutcome Outcome, string OriginalCorrelationId);
public sealed record PurchasingSubcontractTerminalRefusalV1(int Status, string Code,
    IReadOnlyList<PurchasingFieldError> Errors);
public sealed record PurchasingSubcontractReplayV1(StoredPurchasingSubcontractV1? Order,
    PurchasingSubcontractTerminalRefusalV1? Refusal, string OriginalCorrelationId);

public static class PurchasingSubcontractStoreCodesV1
{
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";
    public const string CompanyContextMismatch = "company_context_mismatch";
}

public sealed class PurchasingSubcontractStoreExceptionV1(string code, string message, Exception? inner = null)
    : Exception(message, inner) { public string Code { get; } = code; }
public sealed class PurchasingSubcontractTerminalStoreExceptionV1(PurchasingSubcontractTerminalRefusalV1 refusal)
    : Exception(refusal.Code) { public PurchasingSubcontractTerminalRefusalV1 Refusal { get; } = refusal; }

public interface IPurchasingSubcontractStoreV1
{
    Task<PurchasingSubcontractReplayV1?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken = default);
    Task<PurchasingSubcontractStoreResultV1> StoreDraftAsync(PurchasingWriteEnvelope envelope, string requestHash,
        Func<GoymenERP.Modules.Purchasing.Domain.Subcontracts.SubcontractOrderV1> build,
        CancellationToken cancellationToken = default);
    Task<StoredPurchasingSubcontractV1?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PurchasingSubcontractStoreResultV1?> AdmitAsync(PurchasingWriteEnvelope envelope, string requestHash,
        Guid id, long expectedVersion, IReadOnlyList<PurchasingSubcontractAdmissionSnapshotStoreV1> admissions,
        CancellationToken cancellationToken = default);
}
