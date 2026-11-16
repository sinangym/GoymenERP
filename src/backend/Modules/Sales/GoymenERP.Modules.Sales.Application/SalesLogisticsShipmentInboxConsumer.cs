using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// LOJ-D19, SAT-D285: the Sales inbox consumer of the Logistics definite-shipment events. It mirrors, once and append-only, that a
/// shipment used a single-shipment risk exception (recorded) or that such a shipment was cancelled (the exception drops and a new
/// request and approval are needed). Single use itself is enforced by the Logistics gate in its own transaction; this is only the
/// mirror. Delivery is at-least-once, so the same event (or the same usage under another event id) changes nothing the second time.
/// <list type="bullet">
/// <item>The company is the envelope's, the dispatcher's ambient scope and the payload's at once; any disagreement is poison.</item>
/// <item>A payload that cannot be read, an unknown exception, another instruction's exception, an exception that was never approved
/// or one already bound to another shipment is a broken contract, never retried: <see cref="OutboxPermanentFailureException"/>.</item>
/// <item>A lost race of two deliveries is an ordinary exception: the retry reads the winner and answers "already recorded".</item>
/// </list>
/// Not registered with the dispatcher yet (see <see cref="SalesLogisticsShipmentInboxV1"/>).
/// </summary>
public sealed class SalesLogisticsShipmentInboxConsumer(ISalesRiskExceptionUsageStore store, ICompanyContext companyContext)
    : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(WorkInboxEventV1 inboxEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inboxEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var usage = Read(inboxEvent, companyContext);
        if (usage is null)
        {
            // The shipment used no risk exception: nothing of Sales is about it.
            return;
        }

        var outcome = await store.RecordRiskExceptionUsageAsync(usage, cancellationToken);
        switch (outcome)
        {
            case SalesRiskExceptionUsageOutcome.Recorded:
            case SalesRiskExceptionUsageOutcome.AlreadyRecorded:
                return;
            case SalesRiskExceptionUsageOutcome.ExceptionNotFound:
                throw Poison("risk_exception_not_found", "Olayın adlandırdığı risk istisnası bu şirketin siparişinde yok.");
            case SalesRiskExceptionUsageOutcome.InstructionMismatch:
                throw Poison("risk_exception_instruction_mismatch", "Risk istisnası başka bir yükleme talimatını serbest bırakır.");
            case SalesRiskExceptionUsageOutcome.NotApproved:
                throw Poison("risk_exception_not_approved", "Onaylanmamış risk istisnası bir sevkte kullanılamaz.");
            case SalesRiskExceptionUsageOutcome.ConflictingShipment:
                throw Poison("risk_exception_shipment_conflict", "Risk istisnası başka bir sevke bağlı ya da sevk başka bir istisnayı adlandırıyor.");
            default:
                throw new InvalidOperationException("Risk istisnası kullanım kaydının bilinmeyen sonucu.");
        }
    }

    /// <summary>
    /// Validates the envelope and decodes the payload; null when the shipment used no exception. Every refusal is permanent: the
    /// same event never becomes readable by retrying it.
    /// </summary>
    public static SalesRiskExceptionUsageV1? Read(WorkInboxEventV1 inboxEvent, ICompanyContext companyContext)
    {
        if (!string.Equals(inboxEvent.SourceName, SalesLogisticsShipmentInboxV1.SourceName, StringComparison.Ordinal)
            || inboxEvent.Version != SalesLogisticsShipmentInboxV1.Version
            || SalesLogisticsShipmentInboxV1.KindOf(inboxEvent.EventType) is not { } kind)
        {
            throw Poison("unsupported_event", "Satış bu kaynağın bu olay türünü ya da sürümünü dinlemiyor.");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || inboxEvent.CompanyId != companyContext.CompanyId)
        {
            throw Poison("company_context_mismatch", "Olayın şirketi etkin şirket bağlamıyla aynı değil.");
        }

        if (inboxEvent.EventId == Guid.Empty || string.IsNullOrWhiteSpace(inboxEvent.CorrelationId))
        {
            throw Poison("invalid_envelope", "Olay zarfı kimlik ve correlation taşımalıdır.");
        }

        SalesLogisticsDefiniteShipmentEventV1? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SalesLogisticsDefiniteShipmentEventV1>(inboxEvent.PayloadJson, Json);
        }
        catch (JsonException)
        {
            throw Poison("invalid_payload", "Olay içeriği okunamadı.");
        }

        if (payload is null || payload.CompanyId != inboxEvent.CompanyId || payload.ShipmentId == Guid.Empty
            || payload.SalesOrderId == Guid.Empty || payload.LoadingInstructionId == Guid.Empty
            || payload.RiskExceptionNo is < 1 || payload.RiskExceptionId == Guid.Empty)
        {
            throw Poison("invalid_payload", "Olay içeriği şirket, sevk, sipariş ve yükleme talimatı kimliklerini eksiksiz taşımalıdır.");
        }

        if (payload.RiskExceptionNo is null && payload.RiskExceptionId is null)
        {
            return null;
        }

        return new SalesRiskExceptionUsageV1(kind, payload.ShipmentId, payload.SalesOrderId, payload.LoadingInstructionId,
            payload.RiskExceptionNo, payload.RiskExceptionId, inboxEvent.SourceName, inboxEvent.EventId,
            inboxEvent.OccurredAtUtc.ToUniversalTime(), inboxEvent.CorrelationId.Trim(), inboxEvent.CausationId?.Trim());
    }

    private static OutboxPermanentFailureException Poison(string code, string message) => new(code, message);
}
