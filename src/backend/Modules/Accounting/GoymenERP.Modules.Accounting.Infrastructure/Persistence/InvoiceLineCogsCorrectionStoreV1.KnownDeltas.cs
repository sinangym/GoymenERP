using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1
{
    private async Task RequireCompletedKnownSource(CogsEvidenceIdentityV1 source, Guid salesEventId,
        AccountingInventoryCostComponentV1 component, CancellationToken token)
    {
        var rows = await db.InventoryCostProposals.AsNoTracking().Where(x => x.EventId == salesEventId
            && x.ComponentKey == component.ComponentKey && (x.Status == 3 || x.Status == 4 || x.Status == 5 || x.Status == 6))
            .ToListAsync(token);
        RequireCompany(source.CompanyId);
        if (rows.Count != 1) throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        var accepted = Decode<AccountingInventoryCostProposalV1>(rows[0].ViewJson);
        var exactKnown = component.UnpostedTlAmount ?? component.SignedTlAmount;
        if (accepted.CompanyId != source.CompanyId || accepted.EventId != salesEventId || accepted.InvoiceId != source.InvoiceId
            || accepted.AdmissionId != source.FinalizationAdmissionId || accepted.InvoiceVersion != source.FinalizedVersion
            || accepted.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment
            || Encode(accepted.Component) != Encode(component) || accepted.JournalId != rows[0].JournalId)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        if (accepted.Status is "NoCost" or "AlreadyAccounted")
        {
            if (exactKnown != AccountingInventoryCostAmountsV1.FromDecimal(0m) || accepted.JournalId is not null
                || accepted.Preparation is not null || accepted.ApprovedBy is not null
                || (accepted.Status == "AlreadyAccounted") != component.HasPostedComponents)
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            return; // Proven original zero; no invented zero journal or approval.
        }
        if (accepted.Status is not ("Posted" or "RoundedNoCost")
            || accepted.Preparation is not { Rounding: { } rounding } preparation
            || accepted.ApprovedBy is not { } approvedBy || approvedBy == preparation.PreparedBy
            || accepted.ApprovedAtUtc is null || rounding.ExactSignedAmount != exactKnown
            || !AccountingInventoryCostRoundingV1Rules.TryResolve(component, preparation.CurrencyId, preparation.MoneyProfileId,
                preparation.MoneyProfileVersion, preparation.MoneyScale, out _, out var expected) || expected != rounding)
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
        if (accepted.Status == "RoundedNoCost")
        {
            if (accepted.JournalId is not null || preparation.SignedAmount != "0")
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
            return;
        }
        var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accepted.JournalId, token);
        RequireCompany(source.CompanyId);
        if (journal is null || journal.Kind != (int)JournalKind.Original || journal.CompanyId != source.CompanyId
            || journal.SourceId != source.InvoiceId || journal.CurrencyId != preparation.CurrencyId
            || journal.PostingPurpose != InvoiceLineCogsPostingTraceV1.For(source.InvoiceLineId, source.FinalizedVersion))
            throw Refuse(InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid);
    }
}
