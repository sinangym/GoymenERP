using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class InvoiceLineCogsCorrectionServiceV1 : IInvoiceLineCogsDecisionReadV1
{
    public async Task<AccountingApiResult<InvoiceLineCogsDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); token.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<InvoiceLineCogsDecisionDetailV1>();
        var captured = new ApprovalCapture(companyId, actor, actorText);
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await freshConsumerFence(ct); ct.ThrowIfCancellationRequested();
            await ApprovalFenceAsync(captured, ct); ct.ThrowIfCancellationRequested();
        }
        try
        {
            await Fence(token);
            if (store is not IInvoiceLineCogsDecisionStoreV1 reader) return new(AccountingApiStatus.Unavailable, null, false, null);
            var facts = await reader.ReadCogsDecisionAsync(proposalId, Fence, token);
            // Freeze collection-valued native facts before the final external consumer callback.
            facts = facts is null ? null : facts with { Prepared = Clone(facts.Prepared), Proposal = Clone(facts.Proposal) };
            await Fence(token);
            if (facts is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (!ValidCogsDecisionSource(facts, companyId, proposalId)) return new(AccountingApiStatus.Unavailable, null, false, null);
            if (facts.Proposal.PreparedBy == actor) return Denied<InvoiceLineCogsDecisionDetailV1>();
            var detail = new InvoiceLineCogsDecisionDetailV1(facts.Proposal, facts.Source, facts.CurrentLineVersion,
                CogsSourceFactsDigest(facts), CogsPreviewDigest(facts.Proposal, facts.Source, facts.CurrentLineVersion));
            return new(AccountingApiStatus.Ok, detail, false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return Denied<InvoiceLineCogsDecisionDetailV1>(); }
        catch (JsonException) { return new(AccountingApiStatus.Unavailable, null, false, null); }
    }
    private static InvoiceLineCogsCorrectionViewV1 Clone(InvoiceLineCogsCorrectionViewV1 value) =>
        JsonSerializer.Deserialize<InvoiceLineCogsCorrectionViewV1>(JsonSerializer.Serialize(value, Json), Json)!;
    public static string CogsSourceFactsDigest(InvoiceLineCogsDecisionSourceV1 facts) => AccountingApiService.Sha256(
        JsonSerializer.Serialize(new { facts.Prepared, facts.Source, facts.SourceHash }, Json));
    public static string CogsPreviewDigest(InvoiceLineCogsCorrectionViewV1 proposal, FinalizedInvoiceLineReferenceV1 source, long currentLineVersion) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new { proposal, source, currentLineVersion }, Json));
}
