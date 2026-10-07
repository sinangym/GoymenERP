namespace GoymenERP.Modules.Finance.Contracts;

public enum FinanceOfficialInvoiceImportWorkStateV1
{
    NeedsAction = 1,
    Satisfied = 2,
}

public sealed record FinanceOfficialInvoiceImportWorkObservationV1(
    Guid DeliveryId,
    Guid CompanyId,
    Guid BatchId,
    string SourceKey,
    string ScopeKey,
    long ObservationOrdinal,
    FinanceOfficialInvoiceImportWorkStateV1 DesiredState,
    Guid EvidenceId,
    int PayloadSchemaVersion,
    ReadOnlyMemory<byte> PayloadUtf8,
    string PayloadSha256,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAtUtc);

public interface IFinanceOfficialInvoiceImportWorkObservationSourceV1
{
    Task<ReadResult<FinanceOfficialInvoiceImportWorkObservationV1>> GetAsync(
        FinanceContext context,
        Guid batchId,
        long observationOrdinal,
        Guid deliveryId,
        CancellationToken cancellationToken);
}
