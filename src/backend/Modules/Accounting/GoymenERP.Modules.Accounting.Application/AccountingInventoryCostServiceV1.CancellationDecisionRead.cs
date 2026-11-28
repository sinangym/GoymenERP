using GoymenERP.Modules.Accounting.Application.Contracts;
using System.Text.Json;
using System.Globalization;
namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingInventoryCostServiceV1 : IAccountingInventoryCostCancellationDecisionReadV1
{
    public async Task<AccountingApiResult<AccountingInventoryCostCancellationDecisionDetailV1>> ReadCancellationDecisionAsync(Guid proposalId,
        long? preparedCancellationVersion, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); token.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty || preparedCancellationVersion is <= 0)
            return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<AccountingInventoryCostCancellationDecisionDetailV1>();
        var captured = new ApprovalCapture(companyId, actor, actorText, AccountingJournalReversalPermissionNamesV1.Approve);
        try
        {
            await Fence(token);
            if (store is not IAccountingInventoryCostCancellationDecisionStoreV1 reader) return new(AccountingApiStatus.Unavailable, null, false, null);
            var detail = await reader.ReadCancellationDecisionAsync(proposalId, preparedCancellationVersion, Fence, token);
            await Fence(token);
            if (detail is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (!ValidCancellationDecisionDetail(detail) || detail.OriginalProposal.CompanyId != companyId
                || detail.OriginalProposal.ProposalId != proposalId || preparedCancellationVersion is { } selected && detail.PreparedCancellation.Version != selected)
                return new(AccountingApiStatus.Unavailable, null, false, null);
            if (detail.PreparedCancellation.PreparedBy == actor) return Denied<AccountingInventoryCostCancellationDecisionDetailV1>();
            return new(AccountingApiStatus.Ok, detail, false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingInventoryCostCancellationDecisionDetailV1>(); }
        catch (AccountingStoreException)
        {
            try { await Fence(token); } catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingInventoryCostCancellationDecisionDetailV1>(); }
            return new(AccountingApiStatus.Unavailable, null, false, null);
        }
        async Task Fence(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); await freshConsumerFence(ct); ct.ThrowIfCancellationRequested(); await ApprovalFenceAsync(captured, ct); ct.ThrowIfCancellationRequested(); }
    }
    public static string CancellationSourceFactsDigest(AccountingInventoryCostCancellationDecisionDetailV1 d) =>
        AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(new { d.OriginalProposal, d.PreparedCancellation, d.PreparedRecordedAtUtc, d.SourceCancellation }, Json));
    public static string CancellationPreviewDigest(AccountingInventoryCostCancellationDecisionDetailV1 d) =>
        AccountingInventoryCostSourceValidationV1.Sha256(JsonSerializer.Serialize(new { d.OriginalProposal, d.PreparedCancellation, d.Cancellation, d.PreparedRecordedAtUtc, d.SourceCancellation }, Json));
    public static bool ValidCancellationDecisionDetail(AccountingInventoryCostCancellationDecisionDetailV1? d)
    {
        if (d is not { Pending: not null, OriginalProposal: { } p, PreparedCancellation: { } before, Cancellation: { } c, SourceCancellation: { } source }
            || p.Kind != AccountingInventoryCostKindV1.SalesInvoicedShipment || p.CompanyId == Guid.Empty || p.ProposalId == Guid.Empty || p.Version <= 0
            || !ValidCancellationOriginal(p)
            || p.InvoiceId == Guid.Empty || p.InvoiceVersion <= 0 || p.AdmissionId == Guid.Empty || p.EventId == Guid.Empty || p.Component is null
            || p.CancellationId is not null || p.ReversalProposal is not null || p.ComponentCancellation is not null
            || !AccountingInventoryCostAmountsV1.TryRead(p.Component.SignedTlAmount, out _, out _) || string.IsNullOrWhiteSpace(p.Component.SourceProofJson)
            || p.Component.SourceProofSha256 != AccountingInventoryCostSourceValidationV1.Sha256(p.Component.SourceProofJson)
            || before.Id == Guid.Empty || before.CompanyId != p.CompanyId || before.InventoryCostProposalId != p.ProposalId
            || before.Status != "Prepared" || before.Version <= 0 || before.ApprovedBy is not null || before.ApprovedAtUtc is not null
            || before.PreparedBy == Guid.Empty || before.PreparedAtUtc.Offset != TimeSpan.Zero || d.PreparedRecordedAtUtc.Offset != TimeSpan.Zero
            || before.PeriodId == Guid.Empty || before.AccountingDate == default || before.TransactionDate == default || before.InvoiceLineRevision < 0
            || string.IsNullOrWhiteSpace(before.Reason) || before.Reason != before.Reason.Trim() || before.Reason.Length > 1000
            || before.Components is not { Count: > 0 } || before.MoneyProfiles is not { Count: > 0 }
            || c.Components is not { Count: > 0 } || c.Components.Any(x => x is null)
            || before.Components.Any(x => x is null || x.ReversingJournalId is not null || !(x.EconomicKey is { Length: 64 } && x.EconomicKey.All(Uri.IsHexDigit))
                || string.IsNullOrWhiteSpace(x.Origin) || x.JournalSourceId == Guid.Empty || x.CurrencyId == Guid.Empty
                || x.CogsAccountId == Guid.Empty || x.CounterAccountId == Guid.Empty || x.CogsAccountId == x.CounterAccountId
                || !(x.SourceFingerprint is { Length: 64 } && x.SourceFingerprint.All(Uri.IsHexDigit)) || !CancellationAmount(x.SignedAmount) || !CancellationAmount(x.JournalTotal)
                || x.SignedAmount != "0" && (x.SourceJournalId is null || x.SourceJournalId == Guid.Empty))
            || before.Components.Select(x => x.EconomicKey).Distinct(StringComparer.Ordinal).Count() != before.Components.Count
            || before.MoneyProfiles.Any(x => x is null || x.CurrencyId == Guid.Empty || x.ProfileId == Guid.Empty || x.Version <= 0 || x.Scale is < 0 or > 28)
            || before.MoneyProfiles.Select(x => x.CurrencyId).Distinct().Count() != before.MoneyProfiles.Count
            || before.Components.Any(x => !before.MoneyProfiles.Any(profile => profile.CurrencyId == x.CurrencyId))
            || source.CompanyId != p.CompanyId || source.CancellationId != before.CancellationId || source.CancellationId == Guid.Empty
            || source.SourceDocumentId != p.AdmissionId || source.InvoiceId != p.InvoiceId || source.FinalizedVersion != p.InvoiceVersion
            || source.DocumentKind != AccountingSourceDocumentKindsV1.SalesInvoice || source.CancelledBy == Guid.Empty
            || source.CancelledAtUtc.Offset != TimeSpan.Zero || source.RecordedAtUtc.Offset != TimeSpan.Zero
            || !DecisionDigest(source.RequestHash) || string.IsNullOrWhiteSpace(source.CorrelationId)) return false;
        var normalized = c with { Version = before.Version, Status = "Prepared", Components = c.Components.Select(x => x with { ReversingJournalId = null }).ToArray(), ApprovedBy = null, ApprovedAtUtc = null };
        if (JsonSerializer.Serialize(normalized, Json) != JsonSerializer.Serialize(before, Json)) return false;
        if (c.Status == "Prepared") { if (c.Version != before.Version || c.ApprovedBy is not null || c.ApprovedAtUtc is not null) return false; }
        else if (c.Status != "Approved" || before.Version == long.MaxValue || c.Version != before.Version + 1
            || c.ApprovedBy is not { } checker || checker == Guid.Empty || checker == before.PreparedBy || c.ApprovedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero
            || c.Components.Any(x => x.SignedAmount == "0" ? x.ReversingJournalId is not null : x.ReversingJournalId is null || x.ReversingJournalId == Guid.Empty)
            || d.Pending == true) return false;
        return d.SourceFactsDigest == CancellationSourceFactsDigest(d) && d.PreviewDigest == CancellationPreviewDigest(d);
    }
    private static bool ValidCancellationOriginal(AccountingInventoryCostProposalV1 p) => p.Status switch
    {
        "AwaitingPreparation" or "NoCost" or "AlreadyAccounted" => p.Version == 1 && p.Preparation is null && p.ApprovedBy is null && p.ApprovedAtUtc is null && p.JournalId is null && p.StaleClosure is null,
        "Prepared" or "Posted" or "RoundedNoCost" or "Stale" => ValidInventoryCostPreparedShape(p) && ValidInventoryCostTerminalShape(p) && ValidInventoryCostFrozenAmount(p),
        _ => false,
    };
    private static bool CancellationAmount(string? text) => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var amount) && amount.ToString("0.############################", CultureInfo.InvariantCulture) == text;
}
