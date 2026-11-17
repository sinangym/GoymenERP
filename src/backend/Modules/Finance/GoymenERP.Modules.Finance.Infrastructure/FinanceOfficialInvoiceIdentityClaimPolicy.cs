using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-S5 / FNS-D19 (D5) pure decisions of official identity admission. Reads the documents of an approved v2 file
/// back from its immutable uploaded bytes and decides, per document, whether its official number and ETTN may be
/// claimed. It performs no I/O; the admission service supplies the stored claims and the holders that still have a
/// not-cancelled open item.
/// </summary>
internal static class FinanceOfficialInvoiceIdentityClaimPolicy
{
    internal sealed record PlannedClaim(string Kind, string Value, int Generation, Guid SourceDocumentId);

    internal sealed record Decision(IReadOnlyList<ValidationIssue> Issues, IReadOnlyList<PlannedClaim> Claims);

    /// <summary>
    /// The documents of the uploaded v2 file, re-parsed from its exact bytes; null when the file no longer yields
    /// exactly the approved batch's documents (the stored bytes, checksum, version or document set disagree).
    /// </summary>
    public static IReadOnlyList<FinanceOfficialSalesInvoiceImportDocumentV2>? ReadDocuments(
        FinanceContext context,
        FinanceOfficialInvoiceImportBatchRow upload,
        ApprovedFinanceOpenItemSourceBatch batch)
    {
        try
        {
            var parsed = FinanceOfficialInvoiceImportParserV1.Parse(context,
                new FinanceOfficialInvoiceImportPreviewRequestV1(upload.Id, upload.BatchVersion, upload.FileBytes));
            var preview = parsed.Preview;
            if (!preview.IsValid || preview.Batch is null
                || !FinanceOfficialSalesInvoiceImportTemplateV3.HasDocumentColumns(preview.TemplateVersion)
                || !string.Equals(preview.TemplateVersion, upload.TemplateVersion, StringComparison.Ordinal)
                || preview.Batch.BatchId != batch.BatchId
                || preview.Batch.CompanyId != batch.CompanyId
                || !string.Equals(preview.Batch.BatchVersion, batch.BatchVersion, StringComparison.Ordinal)
                || !string.Equals(preview.SourceSha256, upload.BatchChecksum, StringComparison.Ordinal)
                || !string.Equals(preview.SourceSha256, batch.BatchChecksum, StringComparison.Ordinal))
            {
                return null;
            }

            var documentIds = parsed.Documents.Select(document => document.SourceDocumentId).ToHashSet();
            if (documentIds.Count != parsed.Documents.Count
                || !documentIds.SetEquals(batch.Rows.Select(row => row.SourceDocumentId)))
            {
                return null;
            }
            return parsed.Documents;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The current holder (highest generation) of every claimed identity.</summary>
    public static IReadOnlyDictionary<(string Kind, string Value), FinanceOfficialInvoiceIdentityClaimRow> CurrentHolders(
        IEnumerable<FinanceOfficialInvoiceIdentityClaimRow> claims)
    {
        var holders = new Dictionary<(string Kind, string Value), FinanceOfficialInvoiceIdentityClaimRow>();
        foreach (var claim in claims)
        {
            var key = (claim.IdentityKind, claim.IdentityValue);
            if (!holders.TryGetValue(key, out var current) || claim.Generation > current.Generation)
            {
                holders[key] = claim;
            }
        }
        return holders;
    }

    /// <summary>
    /// Decides the claims of one acceptance. An identity whose holder is another acceptance with a not-cancelled open
    /// item is refused on the first row of the claiming document; an identity already held by this very document is
    /// kept; any other identity gets the next generation.
    /// </summary>
    public static Decision Decide(
        ApprovedFinanceOpenItemSourceBatch batch,
        IReadOnlyList<FinanceOfficialSalesInvoiceImportDocumentV2> documents,
        IReadOnlyDictionary<(string Kind, string Value), FinanceOfficialInvoiceIdentityClaimRow> holders,
        IReadOnlySet<(Guid BatchId, Guid DocumentId)> liveHolders)
    {
        var issues = new List<ValidationIssue>();
        var claims = new List<PlannedClaim>();
        foreach (var document in documents)
        {
            var rowIndex = FirstRowIndex(batch, document.SourceDocumentId);
            Claim(FinanceOfficialInvoiceIdentityKinds.OfficialNo, document.OfficialNo, $"rows[{rowIndex}].officialNo",
                FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoAlreadyOpen);
            Claim(FinanceOfficialInvoiceIdentityKinds.Ettn, document.Ettn, $"rows[{rowIndex}].ettn",
                FinanceOfficialSalesInvoiceImportCodesV2.EttnAlreadyOpen);

            void Claim(string kind, string value, string field, string code)
            {
                if (!holders.TryGetValue((kind, value), out var holder))
                {
                    claims.Add(new PlannedClaim(kind, value, 1, document.SourceDocumentId));
                    return;
                }
                if (holder.HolderBatchId == batch.BatchId && holder.HolderSourceDocumentId == document.SourceDocumentId)
                {
                    return;
                }
                if (liveHolders.Contains((holder.HolderBatchId, holder.HolderSourceDocumentId)))
                {
                    issues.Add(new ValidationIssue(field, code));
                    return;
                }
                claims.Add(new PlannedClaim(kind, value, holder.Generation + 1, document.SourceDocumentId));
            }
        }
        return issues.Count > 0 ? new Decision(issues, []) : new Decision([], claims);
    }

    private static int FirstRowIndex(ApprovedFinanceOpenItemSourceBatch batch, Guid sourceDocumentId)
    {
        for (var index = 0; index < batch.Rows.Count; index++)
        {
            if (batch.Rows[index].SourceDocumentId == sourceDocumentId)
            {
                return index;
            }
        }
        return 0;
    }
}
