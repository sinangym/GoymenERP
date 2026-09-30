using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Fail-closed source selection for FIN-F03 Option A. It publishes only canonical batches returned
/// by the trusted approved-file adapter, after company, shape and fresh batch authority checks.
/// Selecting a row never creates an open item; admission re-reads the exact batch reference through
/// <see cref="IApprovedFinanceOpenItemSourcePort"/> before writing Finance-owned rows.
/// </summary>
public sealed class FinanceOpenItemSourceSelectionService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOpenItemSourceAuthority authority,
    IApprovedFinanceOpenItemSourceCatalogPort catalog) : IFinanceOpenItemSourceSelectionService
{
    private const int MaximumPageSize = 100;
    private const int MaximumCursorLength = 8192;

    public async Task<ReadResult<ApprovedFinanceOpenItemSourcePage>> ListApprovedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context) || !ValidCursor(cursor))
        {
            return Refused(ReadOutcome.Denied);
        }

        var read = await catalog.ListApprovedAsync(context, cursor, cancellationToken);
        if (!SameContext(context))
        {
            return Refused(ReadOutcome.Denied);
        }
        if (read.Outcome != ReadOutcome.Found || read.Resource is not { } page)
        {
            return read.Outcome switch
            {
                ReadOutcome.Denied => Refused(ReadOutcome.Denied),
                ReadOutcome.NotFound => Refused(ReadOutcome.NotFound),
                _ => Refused(ReadOutcome.Unavailable),
            };
        }
        if (page.CompanyId != context.CompanyId || page.Items.Count > MaximumPageSize
            || !ValidCursor(page.NextCursor))
        {
            return Refused(ReadOutcome.Unavailable);
        }

        var seen = new HashSet<Guid>();
        foreach (var batch in page.Items)
        {
            var source = new FinanceOpenItemSourceReference(
                batch.BatchId, batch.BatchVersion, batch.BatchChecksum);
            if (!seen.Add(batch.BatchId)
                || FinanceOpenItemSourcePolicy.ValidateReference(source).Count > 0
                || FinanceOpenItemSourcePolicy.ValidateBatch(context, source, batch).Count > 0)
            {
                return Refused(ReadOutcome.Unavailable);
            }

            var decision = await authority.CheckAsync(context, batch.BatchId, cancellationToken);
            if (decision != PermissionDecision.Allowed || !SameContext(context))
            {
                return Refused(decision == PermissionDecision.Denied
                    ? ReadOutcome.Denied
                    : ReadOutcome.Unavailable);
            }
        }

        return SameContext(context)
            ? new ReadResult<ApprovedFinanceOpenItemSourcePage>(ReadOutcome.Found, page)
            : Refused(ReadOutcome.Denied);
    }

    private static bool ValidCursor(string? cursor) => cursor is null
        || (cursor.Length > 0 && cursor.Length <= MaximumCursorLength
            && string.Equals(cursor, cursor.Trim(), StringComparison.Ordinal));

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);

    private static ReadResult<ApprovedFinanceOpenItemSourcePage> Refused(ReadOutcome outcome) => new(outcome, null);
}
