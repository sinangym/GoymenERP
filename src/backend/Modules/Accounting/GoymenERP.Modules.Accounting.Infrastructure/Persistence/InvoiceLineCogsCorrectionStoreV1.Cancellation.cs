using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1
{
    internal async Task<(long Revision, IReadOnlyList<InventoryCostCancellationComponentV1> Components)>
        ReadCancellationComponentsAsync(AccountingInventoryCostProposalV1 view, CancellationToken token)
    {
        if (view.CompanyId != Company() || view.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment
            || db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        var source = CancellationIdentity(view);
        var latest = await Latest(source, token);
        RequireCompany(source.CompanyId);
        var result = new List<InventoryCostCancellationComponentV1>();
        if (view.JournalId.HasValue)
        {
            var known = await Evidence(source, latest, token);
            RequireCompany(source.CompanyId);
            if (known is not { State: "PostedCogs", Evidence: { } evidence })
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            if (view.Component.HasPostedComponents) await RequireMixedPosting(source, evidence, token);
            var key = "known/" + view.EventId.ToString("D") + "/" + source.InvoiceLineId.ToString("D");
            result.Add(new(AccountingInventoryCostSourceValidationV1.Sha256(key), "Known", evidence.JournalId,
                source.InvoiceId, InvoiceLineCogsPostingTraceV1.For(source.InvoiceLineId, source.FinalizedVersion),
                evidence.CurrencyId, evidence.Amount, evidence.Amount, evidence.CogsAccountId, evidence.CounterAccountId,
                view.Component.SourceProofSha256));
        }
        foreach (var value in await AttributedEvidence(source, token) ?? [])
        {
            var share = value.Source;
            result.Add(new(share.Fingerprint, "Manufacturing", value.CurrentJournalId, share.SourceEventId,
                ManufacturingCostDifferenceInboxV1.PostingPurpose, share.CurrencyId, value.CurrentJournalTotal,
                share.SignedAmount, value.CurrentCogsAccountId, value.CounterAccountId, share.Fingerprint));
        }
        foreach (var value in await PurchaseAttributedEvidence(source, token) ?? [])
        {
            var share = value.Source;
            result.Add(new(share.Fingerprint, "Purchase", value.CurrentJournalId, share.Approval.ProposalId,
                AccountingInventoryCostTraceV1.PurchaseDifferencePurpose, share.Approval.CurrencyId,
                value.CurrentJournalTotal, share.SignedAmount, value.CurrentCogsAccountId, value.CounterAccountId, share.Fingerprint));
        }
        RequireCompany(source.CompanyId);
        if (result.Select(x => x.EconomicKey).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        return (latest?.Version ?? 0, result.OrderBy(x => x.EconomicKey, StringComparer.Ordinal).ToArray());
    }

    private static CogsEvidenceIdentityV1 CancellationIdentity(AccountingInventoryCostProposalV1 view)
    {
        try
        {
            var component = view.Component;
            using var document = JsonDocument.Parse(component.SourceProofJson);
            var root = document.RootElement;
            var claim = root.GetProperty("claim");
            var shipment = claim.GetProperty("shipmentId").GetGuid();
            var dispatch = claim.GetProperty("dispatchNoteId").GetGuid();
            if (component.InvoiceLineId is not { } line || line == Guid.Empty || shipment == Guid.Empty || dispatch == Guid.Empty
                || root.GetProperty("companyId").GetGuid() != view.CompanyId
                || root.GetProperty("invoiceId").GetGuid() != view.InvoiceId || root.GetProperty("lineId").GetGuid() != line
                || root.GetProperty("version").GetInt64() != view.InvoiceVersion
                || root.GetProperty("admissionId").GetGuid() != view.AdmissionId
                || claim.GetProperty("productId").GetGuid() != component.ProductId
                || component.SalesPhysicalClaims is not { Count: > 0 } physical
                || physical.Any(x => x.ShipmentId != shipment || x.ProductId != component.ProductId)
                || component.SourceProofSha256 != AccountingInventoryCostSourceValidationV1.Sha256(component.SourceProofJson))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            return new(view.CompanyId, view.InvoiceId, line, view.AdmissionId, view.InvoiceVersion,
                component.ProductId, shipment, dispatch, component.SourceProofSha256, true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid); }
    }
}
