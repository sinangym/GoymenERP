namespace GoymenERP.Modules.Sales.Domain.Invoices;

public sealed partial class SalesInvoice
{
    /// <summary>SAT-D96: only a trusted exact external cancellation proof permits this terminal transition.
    /// The original match, amounts, approval, finalization and Finance offer are retained unchanged.</summary>
    public void RecordOfficialCancellation(long expectedVersion, string actorId, string reason,
        SalesOfficialInvoiceCancellationEvidenceV1 evidence, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireState(SalesInvoiceState.Finalized, "Yalnız kesinleşmiş faturanın resmî iptali kaydedilir.");
        if (Match is null || Finalization is null || evidence is null
            || !evidence.IsValidFor(CompanyId, Id, Match.OfficialNo, Match.Ettn)
            || now == default
            || evidence.OfficiallyCancelledAtUtc > now.ToUniversalTime())
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Gerçek dış iptal kanıtı fatura ile uyuşmuyor.");
        var text = RequireReason(reason);
        if (text is null) throw new ArgumentException("İptal gerekçesi zorunludur.", nameof(reason));
        Decision = Decision with { CancelledBy = RequireActor(actorId), CancelledAtUtc = now.ToUniversalTime(), CancellationReason = text };
        State = SalesInvoiceState.OfficiallyCancelled;
        Advance();
    }
}

public sealed partial class SalesSubcontractServiceInvoice
{
    public void RecordOfficialCancellation(long expectedVersion, string actorId, string reason,
        SalesOfficialInvoiceCancellationEvidenceV1 evidence, DateTimeOffset now)
    {
        RequireLifecycleVersion(expectedVersion);
        RequireLifecycleState(SalesSubcontractServiceInvoiceState.Finalized);
        if (Match is null || Finalization is null || evidence is null
            || !evidence.IsValidFor(CompanyId, Id, Match.OfficialNo, Match.Ettn)
            || now == default
            || evidence.OfficiallyCancelledAtUtc > now.ToUniversalTime())
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Gerçek dış iptal kanıtı fason fatura ile uyuşmuyor.");
        var text = Reason(reason);
        if (text is null) throw new ArgumentException("İptal gerekçesi zorunludur.", nameof(reason));
        Decision = Decision with { CancelledBy = Actor(actorId), CancelledAtUtc = now.ToUniversalTime(), CancellationReason = text };
        State = SalesSubcontractServiceInvoiceState.OfficiallyCancelled;
        AdvanceLifecycle();
    }
}
