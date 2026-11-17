using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1
{
    private async Task<IReadOnlyList<FinanceOfficialInvoiceSourceDifferenceV1>?> SourceDifferencesAsync(
        FinanceContext context, FinanceOfficialSalesInvoiceRow row, CancellationToken cancellationToken)
    {
        var claims = await dbContext.OfficialInvoiceIdentityClaims.AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && (x.IdentityKind == FinanceOfficialInvoiceIdentityKinds.OfficialNo
                && x.IdentityValue == row.OfficialNo || x.IdentityKind == FinanceOfficialInvoiceIdentityKinds.Ettn && x.IdentityValue == row.Ettn))
            .ToListAsync(cancellationToken);
        var holderIds = FinanceOfficialInvoiceIdentityClaimPolicy.CurrentHolders(claims).Values
            .Select(x => x.HolderBatchId).Distinct().ToArray();
        if (holderIds.Length == 0) return [];
        var uploads = await (from upload in dbContext.OfficialInvoiceImportBatches.AsNoTracking()
                             join approval in dbContext.OfficialInvoiceImportApprovals.AsNoTracking() on upload.Id equals approval.BatchId
                             where holderIds.Contains(upload.Id) && upload.CompanyId == context.CompanyId
                                 && approval.CompanyId == context.CompanyId
                             select upload).ToListAsync(cancellationToken);
        if (uploads.Count != holderIds.Length) return null;
        var result = new List<FinanceOfficialInvoiceSourceDifferenceV1>();
        foreach (var upload in uploads)
        {
            if (upload.CompanyId != context.CompanyId) return null;
            var sourceRows = JsonSerializer.Deserialize<ApprovedFinanceOpenItemSourceRow[]>(upload.SourceRowsJson, Json);
            if (sourceRows is null) return null;
            var batch = new ApprovedFinanceOpenItemSourceBatch(upload.Id, context.CompanyId, upload.BatchVersion,
                upload.BatchChecksum, sourceRows);
            var documents = FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(context, upload, batch);
            if (documents is null) return null;
            foreach (var difference in FinanceOfficialInvoiceSourceComparison.Compare([row],
                         FinanceOfficialInvoiceSourceComparison.Imported(batch, documents)))
            {
                var imported = difference.Imported;
                result.Add(new(imported.BatchId, imported.SourceDocumentId, imported.OfficialNo, imported.Ettn,
                    difference.Fields, imported.InvoiceDate, imported.PayableFunctionalAmount,
                    row.Status == (int)FinanceOfficialSalesInvoiceStatusV1.Approved ? "manual" : "approved-import"));
            }
        }
        return result.OrderBy(x => x.ImportBatchId).ThenBy(x => x.SourceDocumentId).ToArray();
    }
}
