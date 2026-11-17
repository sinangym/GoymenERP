using GoymenERP.Modules.Sales.Domain.Invoices;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Sales.Application.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SalesOfficialInvoiceKindV1>))]
public enum SalesOfficialInvoiceKindV1 { Normal = 1, Subcontract = 2 }
public enum SalesOfficialCancellationEvidenceOutcomeV1 { Available = 1, NotFound = 2, Unavailable = 3 }

/// <summary>Trusted owner answer, never read from the command body or inferred from a user's declaration.
/// Available requires the actual external cancellation status source; an invoice number, uploaded file or
/// permission alone is not external cancellation evidence.</summary>
public sealed record SalesOfficialCancellationEvidenceAnswerV1(SalesOfficialCancellationEvidenceOutcomeV1 Outcome,
    SalesOfficialInvoiceCancellationEvidenceV1? Evidence);

public interface ISalesOfficialCancellationEvidenceReadPortV1
{
    Task<SalesOfficialCancellationEvidenceAnswerV1> ReadAsync(Guid companyId, SalesOfficialInvoiceKindV1 kind,
        Guid invoiceId, string officialInvoiceNo, string ettn, CancellationToken cancellationToken = default);
}

/// <summary>Q12's external status provider is not configured. This is a real unavailable boundary, not a successful assertion.</summary>
public sealed class UnavailableSalesOfficialCancellationEvidenceReadPortV1 : ISalesOfficialCancellationEvidenceReadPortV1
{
    public Task<SalesOfficialCancellationEvidenceAnswerV1> ReadAsync(Guid companyId, SalesOfficialInvoiceKindV1 kind,
        Guid invoiceId, string officialInvoiceNo, string ettn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SalesOfficialCancellationEvidenceAnswerV1(
            SalesOfficialCancellationEvidenceOutcomeV1.Unavailable, null));
    }
}

public sealed record RecordSalesOfficialInvoiceCancellationV1(string ExpectedVersion, string Reason);
public sealed record RetrySalesOfficialInvoiceCancellationFinanceV1(long ExpectedDeliveryVersion, bool ReleaseActiveAllocations = false);

/// <summary>Price-free command receipt. No Finance or Accounting effect is implied by recording the Sales fact.</summary>
public sealed record SalesOfficialInvoiceCancellationViewV1(Guid CompanyId, Guid CancellationId,
    Guid InvoiceId, SalesOfficialInvoiceKindV1 Kind, string InvoiceVersion, string FinalizedVersion, Guid AdmissionId,
    string OfficialInvoiceNo, string Ettn, string RecordedBy, DateTimeOffset RecordedAtUtc, string Reason,
    SalesOfficialInvoiceCancellationEvidenceV1 Evidence, long DeliveryVersion = 0, string FinanceStatus = "Pending",
    string? FinanceReason = null);

public sealed record SalesOfficialInvoiceCancellationResultV1(SalesOfficialInvoiceCancellationViewV1 Cancellation,
    bool IsReplay, string OriginalCorrelationId);

/// <summary>The original immutable Finance offer. The retry uses these exact values and source identities;
/// it never asks current Sales prices, quantities, profiles or exchange rates to reconstruct a past event.</summary>
public sealed record SalesOfficialCancellationAdmissionV1(Guid AdmissionId, Guid InvoiceId, string InvoiceVersion,
    Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, decimal? ExchangeRate,
    IReadOnlyList<SalesFinanceInstalmentV1> Instalments, bool WasAdmitted, string OfficialInvoiceNo, string Ettn);

public sealed record SalesOfficialCancellationTargetV1(Guid CompanyId, Guid InvoiceId, SalesOfficialInvoiceKindV1 Kind,
    long Version, string OfficialInvoiceNo, string Ettn, SalesOfficialCancellationAdmissionV1 Admission);

public sealed record SalesOfficialCancellationDeliveryV1(SalesOfficialInvoiceCancellationViewV1 Cancellation,
    SalesOfficialCancellationAdmissionV1 Admission);

public sealed record SalesFinanceCancellationRequestV1(Guid CompanyId, string ActorId, string CorrelationId,
    string? CausationId, string IdempotencyKey, Guid CancellationId, string Reason,
    SalesOfficialCancellationAdmissionV1 Admission, bool ReleaseActiveAllocations);

public sealed record SalesFinanceCancellationAnswerV1(string Status, string? Reason = null);

public interface ISalesFinanceCancellationPortV1
{
    Task<SalesFinanceCancellationAnswerV1> CancelAsync(SalesFinanceCancellationRequestV1 request,
        CancellationToken cancellationToken = default);
}

public static class SalesOfficialInvoiceCancellationCommandsV1
{
    public const string Record = "sales.invoice.official-cancellation.record.v1";
    public const string RetryFinance = "sales.invoice.official-cancellation.finance-retry.v1";
    public const string EventType = "sales.invoice.cancelled.v1";
}

public sealed record SalesOfficialCancellationCandidateV1(Guid InvoiceId, SalesOfficialInvoiceKindV1 Kind,
    string DocumentNo, string Version, string State, string OfficialInvoiceNo, string Ettn);
public sealed record SalesOfficialCancellationCandidatesV1(IReadOnlyList<SalesOfficialCancellationCandidateV1> Items, string? NextCursor);

public interface ISalesOfficialInvoiceCancellationStoreV1
{
    Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 kind, Guid? after,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationResultV1?> FindReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOfficialCancellationTargetV1?> ReadTargetAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        SalesOfficialInvoiceKindV1 kind, Guid invoiceId, long expectedVersion, string reason,
        SalesOfficialInvoiceCancellationEvidenceV1 evidence, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default);
    Task<SalesOfficialCancellationDeliveryV1?> ReadForInvoiceAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOfficialCancellationDeliveryV1?> ReadDeliveryAsync(Guid cancellationId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationResultV1> RecordDeliveryAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid cancellationId, long expectedDeliveryVersion, bool releaseActiveAllocations, SalesFinanceCancellationAnswerV1 answer,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationResultV1> RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, SalesDraftTerminalRefusal refusal, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default);
}

public interface ISalesOfficialInvoiceCancellationServiceV1
{
    Task<SalesOfficialCancellationCandidatesV1> ListCandidatesAsync(SalesOfficialInvoiceKindV1 kind, string? cursor,
        CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationResultV1> RecordAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId,
        RecordSalesOfficialInvoiceCancellationV1 request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationViewV1> GetAsync(Guid cancellationId, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationViewV1> GetForInvoiceAsync(SalesOfficialInvoiceKindV1 kind, Guid invoiceId, CancellationToken cancellationToken = default);
    Task<SalesOfficialInvoiceCancellationResultV1> RetryFinanceAsync(Guid cancellationId,
        RetrySalesOfficialInvoiceCancellationFinanceV1 request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}
