using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

public static class AccountingSalesInvoiceCancelledInboxV1
{
    public const string ConsumerName = "accounting.sales-invoice-cancellation";
    public const string SourceName = "sales";
    public const string EventType = "sales.invoice.cancelled.v1";
    public const int Version = 1;
    public const string ActorId = "inbox." + ConsumerName;
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } = [new(SourceName, EventType, Version)];
}

/// <summary>Only committed, previously finalized cancellations are published; draft cancellation is not a financial source.</summary>
public sealed record AccountingSalesInvoiceCancelledEventV1(Guid CompanyId, Guid CancellationId, Guid InvoiceId,
    Guid AdmissionId, long FinalizedVersion, Guid CancelledBy, DateTimeOffset CancelledAtUtc, string Reason);

public sealed class AccountingSalesInvoiceCancelledInboxConsumerV1(IAccountingSourceInvoiceCancellationPortV1 cancellations,
    ICompanyContext company) : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task HandleAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken)
    {
        var request = Read(envelope, company);
        var result = await cancellations.CancelAsync(request, cancellationToken);
        if (!company.IsResolved || company.CompanyId != request.CompanyId)
            throw new AccountingInvoiceJournalRetryException("company_context_mismatch", "Muhasebe iptal kabulü sırasında şirket bağlamı değişti.");
        if (result.Status == "Recorded") return;
        if (result.Status is "Invalid" or "Conflict") throw new OutboxPermanentFailureException("invoice_cancellation_invalid",
            "Muhasebe kaynak fatura iptal kanıtını kabul edemedi.");
        throw new AccountingInvoiceJournalRetryException("invoice_cancellation_unavailable", "Kaynak iptal kabulü aynı olayla yeniden denenecek.");
    }

    public static AccountingSourceInvoiceCancellationV1 Read(WorkInboxEventV1 envelope, ICompanyContext company)
    {
        if (envelope.SourceName != AccountingSalesInvoiceCancelledInboxV1.SourceName
            || envelope.EventType != AccountingSalesInvoiceCancelledInboxV1.EventType || envelope.Version != 1
            || envelope.EventId == Guid.Empty || !company.IsResolved || envelope.CompanyId != company.CompanyId)
            throw new OutboxPermanentFailureException("invoice_cancellation_envelope_invalid", "Fatura iptal zarfı uyuşmuyor.");
        AccountingSalesInvoiceCancelledEventV1? payload;
        try { payload = JsonSerializer.Deserialize<AccountingSalesInvoiceCancelledEventV1>(envelope.PayloadJson, Json); }
        catch (JsonException) { throw new OutboxPermanentFailureException("invoice_cancellation_payload_invalid", "Fatura iptal kaynağı okunamadı."); }
        if (payload is null || payload.CompanyId != envelope.CompanyId || payload.CancellationId != envelope.EventId)
            throw new OutboxPermanentFailureException("invoice_cancellation_payload_invalid", "Fatura iptal kimliği uyuşmuyor.");
        var request = new AccountingSourceInvoiceCancellationV1(payload.CompanyId, AccountingSalesInvoiceCancelledInboxV1.ActorId,
            envelope.CorrelationId, envelope.CausationId ?? envelope.EventId.ToString("D"), payload.CancellationId,
            AccountingSourceDocumentKindsV1.SalesInvoice, payload.AdmissionId, payload.InvoiceId, payload.FinalizedVersion,
            payload.CancelledBy, payload.CancelledAtUtc, payload.Reason);
        if (!AccountingSourceInvoiceCancellationServiceV1.Valid(request))
            throw new OutboxPermanentFailureException("invoice_cancellation_payload_invalid", "Fatura iptal kanıtı eksik.");
        return request;
    }
}
