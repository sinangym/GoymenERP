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
            return Refused(ReadOutcome.Denied);
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
                row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);

        var after = await authority.CheckAsync(context, batchId, cancellationToken);
        if (after != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused(ReadOutcome.Denied);
        }

        if (rows.Count == 0)
        {
            return Refused(ReadOutcome.NotFound);
        }

        var first = rows[0];
        if (rows.Any(row => row.CompanyId != context.CompanyId
                || row.SourceBatchId != batchId
                || !string.Equals(row.SourceBatchVersion, first.SourceBatchVersion, StringComparison.Ordinal)
                || !string.Equals(row.SourceBatchChecksum, first.SourceBatchChecksum, StringComparison.Ordinal)))
        {
            return Refused(ReadOutcome.Unavailable);
        }

        return new(ReadOutcome.Found, new FinanceOfficialInvoiceAppliedBatchV1(
            batchId,
            context.CompanyId,
            first.SourceBatchVersion,
            first.SourceBatchChecksum,
            rows));
    }

    private bool SameContext(FinanceContext context) =>
        companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    private static ReadResult<FinanceOfficialInvoiceAppliedBatchV1> Refused(ReadOutcome outcome) =>
        new(outcome, null);
}
