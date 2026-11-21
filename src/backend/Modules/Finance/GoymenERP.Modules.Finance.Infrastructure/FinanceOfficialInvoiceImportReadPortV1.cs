using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed class FinanceOfficialInvoiceImportReadPortV1(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOpenItemSourceAuthority authority)
    : IFinanceOfficialInvoiceImportReadPortV1
{
    public async Task<ReadResult<FinanceOfficialInvoiceAppliedBatchV1>> ReadAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (batchId == Guid.Empty || !SameContext(context))
        {
            return Refused(ReadOutcome.Denied);
        }

        var before = await authority.CheckAsync(context, batchId, cancellationToken);
        if (before != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused(!SameContext(context) ? ReadOutcome.Denied : Refusal(before));
        }

        var rows = await dbContext.OpenItems.AsNoTracking()
            .Where(row => row.SourceBatchId == batchId)
            .OrderBy(row => row.Id)
            .Select(row => new FinanceOfficialInvoiceOpenItemV1(
                row.Id,
                row.CompanyId,
                row.SourceKind,
                row.SourceId,
                row.SourceLineId,
                row.SourceVersion,
                row.SourceBatchId,
                row.SourceBatchVersion,
                row.SourceBatchChecksum,
                row.PartyId,
                row.CompanyAccountId,
                row.CurrencyId,
                row.OriginalAmountText,
                row.OutstandingAmountText,
                row.DueOn,
                row.Status,
                row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), null))
            .ToListAsync(cancellationToken);

        var ids = rows.Select(x => x.Id).ToArray();
        var assignments = await dbContext.PartyBalanceAssignments.AsNoTracking()
            .Where(x => x.Kind == FinanceBalanceAssignmentKinds.OpenItem && ids.Contains(x.SourceId)).ToListAsync(cancellationToken);
        try
        {
            rows = rows.Select(row => row with { AllocationPositions = new FinanceAllocationPositions(row.PartyId,
                row.CompanyAccountId, row.CurrencyId, row.OutstandingAmount, assignments.Where(x => x.SourceId == row.Id).ToArray())
                .All().Select(x => new FinanceAllocationPositionV1(x.PartyId, x.CompanyAccountId, x.AvailableAmount,
                    x.AssignmentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), x.TransferId)).ToArray() }).ToList();
        }
        catch (InvalidOperationException) { return Refused(ReadOutcome.Unavailable); }

        IReadOnlyList<FinanceOfficialInvoiceImportedDocumentViewV1>? documents;
        try { documents = await ReadDocuments(context, batchId, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { documents = null; }
        var after = await authority.CheckAsync(context, batchId, cancellationToken);
        if (after != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused(!SameContext(context) ? ReadOutcome.Denied : Refusal(after));
        }

        if (rows.Count == 0)
        {
            return Refused(ReadOutcome.NotFound);
        }
        if (documents is null) return Refused(ReadOutcome.Unavailable);

        var first = rows[0];
        if (rows.Any(row => row.CompanyId != context.CompanyId
                || row.SourceBatchId != batchId
                || !string.Equals(row.SourceBatchVersion, first.SourceBatchVersion, StringComparison.Ordinal)
                || !string.Equals(row.SourceBatchChecksum, first.SourceBatchChecksum, StringComparison.Ordinal)))
        {
            return Refused(ReadOutcome.Unavailable);
        }

        return new ReadResult<FinanceOfficialInvoiceAppliedBatchV1>(ReadOutcome.Found, new FinanceOfficialInvoiceAppliedBatchV1(
            batchId,
            context.CompanyId,
            first.SourceBatchVersion,
            first.SourceBatchChecksum,
            rows, documents));
    }

    private async Task<IReadOnlyList<FinanceOfficialInvoiceImportedDocumentViewV1>?> ReadDocuments(
        FinanceContext context, Guid batchId, CancellationToken cancellationToken)
    {
        var upload = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == batchId, cancellationToken);
        if (upload is null || !FinanceOfficialSalesInvoiceImportTemplateV3.HasDocumentColumns(upload.TemplateVersion)) return [];
        var sourceRows = JsonSerializer.Deserialize<ApprovedFinanceOpenItemSourceRow[]>(upload.SourceRowsJson, FinanceJson.CreateOptions());
        if (sourceRows is null) return null;
        var batch = new ApprovedFinanceOpenItemSourceBatch(upload.Id, context.CompanyId, upload.BatchVersion, upload.BatchChecksum, sourceRows);
        var documents = FinanceOfficialInvoiceIdentityClaimPolicy.ReadDocuments(context, upload, batch);
        if (documents is null) return null;
        var numbers = documents.Select(row => row.OfficialNo).ToArray();
        var identities = documents.Select(row => row.Ettn).ToArray();
        var manual = await dbContext.OfficialSalesInvoices.AsNoTracking()
            .Where(row => (row.Status == 1 || row.Status == 2) && (numbers.Contains(row.OfficialNo) || identities.Contains(row.Ettn)))
            .ToListAsync(cancellationToken);
        var result = new List<FinanceOfficialInvoiceImportedDocumentViewV1>();
        foreach (var imported in FinanceOfficialInvoiceSourceComparison.Imported(batch, documents))
        {
            var matching = manual.Where(row => row.OfficialNo == imported.OfficialNo || row.Ettn == imported.Ettn).ToArray();
            var approved = matching.Where(row => row.Status == 2).ToArray();
            if (approved.Length > 1) return null;
            var winner = approved.SingleOrDefault();
            result.Add(new FinanceOfficialInvoiceImportedDocumentViewV1(imported.SourceDocumentId, imported.OfficialNo, imported.Ettn, imported.InvoiceDate,
                imported.PayableFunctionalAmount, matching.Any(row => row.Status == 1), winner?.Id,
                winner?.PayableFunctionalAmountText ?? imported.PayableFunctionalAmount,
                winner is null ? [] : FinanceOfficialInvoiceSourceComparison.Differences(winner, imported)));
        }
        return result;
    }

    private bool SameContext(FinanceContext context) =>
        companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    private static ReadResult<FinanceOfficialInvoiceAppliedBatchV1> Refused(ReadOutcome outcome) =>
        new ReadResult<FinanceOfficialInvoiceAppliedBatchV1>(outcome, null);

    private static ReadOutcome Refusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied;
}
