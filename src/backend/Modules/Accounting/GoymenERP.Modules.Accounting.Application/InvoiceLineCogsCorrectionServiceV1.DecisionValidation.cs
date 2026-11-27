using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class InvoiceLineCogsCorrectionServiceV1
{
    public static bool ValidCogsDecisionDetail(InvoiceLineCogsDecisionDetailV1? detail) => detail is not null
        && ValidCogsDecisionView(detail.Proposal, detail.Source) && detail.CurrentLineVersion >= detail.Proposal.ExpectedLineVersion
        && (detail.Proposal.Status != "Approved" || detail.Proposal.ExpectedLineVersion < long.MaxValue && detail.CurrentLineVersion >= detail.Proposal.ExpectedLineVersion + 1)
        && CogsDigest(detail.SourceFactsDigest) && detail.PreviewDigest == CogsPreviewDigest(detail.Proposal, detail.Source, detail.CurrentLineVersion);
    public static bool ValidCogsDecisionSource(InvoiceLineCogsDecisionSourceV1? facts, Guid companyId, Guid proposalId) => facts is not null
        && facts.Prepared is { Status: "Prepared", Version: 1 } p && facts.Proposal is { } current
        && p.CompanyId == companyId && p.ProposalId == proposalId && current.CompanyId == companyId && current.ProposalId == proposalId
        && ValidCogsDecisionView(p, facts.Source) && ValidCogsDecisionView(current, facts.Source) && CogsDigest(facts.SourceHash)
        && facts.CurrentLineVersion >= p.ExpectedLineVersion
        && (current.Status == "Prepared" ? !facts.RevisionBound && JsonSerializer.Serialize(p, Json) == JsonSerializer.Serialize(current, Json)
            : facts.RevisionBound && p.ExpectedLineVersion < long.MaxValue && facts.CurrentLineVersion >= p.ExpectedLineVersion + 1
                && JsonSerializer.Serialize(p, Json) == JsonSerializer.Serialize(PreparedCogsFacts(current), Json));
    private static InvoiceLineCogsCorrectionViewV1 PreparedCogsFacts(InvoiceLineCogsCorrectionViewV1 approved) => approved with
    { Version = 1, Status = "Prepared", ApprovedBy = null, ApprovedAtUtc = null, ReversingCorrectionJournalId = null,
        ReplacementCorrectionJournalId = null, AttributedCorrections = null };
    private static bool ValidCogsDecisionView(InvoiceLineCogsCorrectionViewV1? p, FinalizedInvoiceLineReferenceV1? s) => p is not null && s is not null
        && p.ProposalId != Guid.Empty && p.CompanyId != Guid.Empty && p.CompanyId == s.CompanyId && p.InvoiceId == s.InvoiceId && p.InvoiceLineId == s.InvoiceLineId
        && s.InvoiceId != Guid.Empty && s.InvoiceLineId != Guid.Empty && s.FinalizationAdmissionId != Guid.Empty && s.FinalizedVersion > 0
        && p.FinalizedVersion == s.FinalizedVersion && s.ProductId != Guid.Empty && s.ShipmentId != Guid.Empty && s.DispatchNoteId != Guid.Empty
        && s.DispatchNoteLineNo > 0 && s.InvoiceLineNo > 0 && s.InvoiceDate != default && !string.IsNullOrWhiteSpace(s.OfficialInvoiceNumber)
        && s.Ettn != Guid.Empty && ValidFingerprint(s.Fingerprint) && p.ExpectedLineVersion >= 0 && Enum.IsDefined(p.Classification)
        && p.PeriodId != Guid.Empty && p.TransactionDate != default && p.AccountingDate != default && p.ValueDate != DateOnly.MinValue
        && p.PreparedBy != Guid.Empty && p.PreparedAtUtc.Offset == TimeSpan.Zero && p.AccountPlan is { } a && a.PackageId != Guid.Empty
        && a.PackageVersion > 0 && (a.CatalogVersion is null or > 0) && !string.IsNullOrWhiteSpace(a.SourceVersion) && ValidFingerprint(a.SourceSha256)
        && a.Account is { } account && account.AccountId != Guid.Empty && !string.IsNullOrWhiteSpace(account.Code) && !string.IsNullOrWhiteSpace(account.Name)
        && !string.IsNullOrWhiteSpace(p.Reason) && p.Reason == p.Reason.Trim() && p.Reason.Length <= 1000
        && (p.PostedEvidence is null || p.PostedEvidence.CompanyId == p.CompanyId && p.PostedEvidence.InvoiceId == p.InvoiceId
            && p.PostedEvidence.InvoiceLineId == p.InvoiceLineId && p.PostedEvidence.FinalizedVersion == p.FinalizedVersion
            && p.PostedEvidence.JournalId != Guid.Empty && p.PostedEvidence.CogsAccountId != Guid.Empty && p.PostedEvidence.CounterAccountId != Guid.Empty
            && p.PostedEvidence.CurrencyId != Guid.Empty && !string.IsNullOrWhiteSpace(p.PostedEvidence.Amount) && ValidFingerprint(p.PostedEvidence.SourceFingerprint))
        && (p.AttributedEvidence is null || p.AttributedEvidence.All(e => e is not null && e.Source is { } m && m.CompanyId == p.CompanyId
            && m.Shipment is { } shipment && shipment.ShipmentId == s.ShipmentId && shipment.ProductId == s.ProductId
            && e.CurrentJournalId != Guid.Empty && e.CurrentCogsAccountId != Guid.Empty && e.CounterAccountId != Guid.Empty))
        && (p.PurchaseAttributedEvidence is null || p.PurchaseAttributedEvidence.All(e => e is not null && e.Source is { } m
            && m.Approval is { } approval && approval.CompanyId == p.CompanyId && m.ShipmentId == s.ShipmentId && m.ProductId == s.ProductId
            && m.Candidates is not null && m.Candidates.All(c => c is not null && c.CompanyId == p.CompanyId && c.ProductId == s.ProductId)
            && e.CurrentCogsAccountId != Guid.Empty && e.CounterAccountId != Guid.Empty))
        && (p.AttributedCorrections is null || p.AttributedCorrections.All(e => e is not null && ValidFingerprint(e.AttributionFingerprint)
            && e.SourceJournalId != Guid.Empty && e.ReversingCorrectionJournalId != Guid.Empty && e.ReplacementCorrectionJournalId != Guid.Empty))
        && (p.Status == "Prepared" && p.Version == 1 && p.ApprovedBy is null && p.ApprovedAtUtc is null
                && p.ReversingCorrectionJournalId is null && p.ReplacementCorrectionJournalId is null && p.AttributedCorrections is null
            || p.Status == "Approved" && p.Version == 2 && p.ApprovedBy is { } checker && checker != Guid.Empty && checker != p.PreparedBy
                && p.ApprovedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero
                && (p.ReversingCorrectionJournalId is null && p.ReplacementCorrectionJournalId is null
                    || p.ReversingCorrectionJournalId is { } reverse && reverse != Guid.Empty && p.ReplacementCorrectionJournalId is { } replace && replace != Guid.Empty));
    private static bool CogsDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
