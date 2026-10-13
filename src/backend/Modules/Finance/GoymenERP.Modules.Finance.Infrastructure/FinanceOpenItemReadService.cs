using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FI04 list/detail over admitted open items. The fixed keyset ordering is <c>CreatedAtUtc desc, Id desc</c>;
/// the opaque cursor is bound to that ordering, the all-items query, company and actor. The technical page bound
/// limits response size only: there is no amount filter, amount ordering, total, difference or other monetary
/// derivative. Reads are no-tracking and never call <c>SaveChanges</c>.
/// <para>
/// Metadata and amount authorities are measured independently before and after the database await. Loss of metadata
/// read refuses the whole response. Loss of amount authority preserves the metadata response but returns both stored
/// amounts redacted; an unavailable amount authority returns both amounts unavailable. Visibility requires Allowed at
/// both measurements, so a stale earlier grant never releases money.
/// </para>
/// </summary>
public sealed class FinanceOpenItemReadService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions) : IFinanceOpenItemReadServiceV1
{
    internal const int PageSize = 50;

    public async Task<ReadResult<FinanceOpenItemPageV1>> ListAuthorizedAsync(
        FinanceContext context,
        SourceListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused<FinanceOpenItemPageV1>(ReadOutcome.Denied);
        }

        var readBefore = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItems, null, cancellationToken);
        if (readBefore != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused<FinanceOpenItemPageV1>(ReadRefusal(readBefore));
        }

        (DateTimeOffset CreatedAtUtc, Guid Id)? after = null;
        if (request.Cursor is not null)
        {
            after = FinanceOpenItemReadCursor.Decode(request.Cursor, context.CompanyId, context.ActorId);
            if (after is null)
            {
                return Refused<FinanceOpenItemPageV1>(ReadOutcome.NotFound);
            }
        }

        var amountBefore = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItemAmount, null, cancellationToken);
        if (!SameContext(context))
        {
            return Refused<FinanceOpenItemPageV1>(ReadOutcome.Denied);
        }

        var query = dbContext.OpenItems.AsNoTracking()
            .Where(row => row.CompanyId == context.CompanyId);
        if (after is { } key)
        {
            var afterCreatedAtUtc = key.CreatedAtUtc;
            var afterId = key.Id;
            query = query.Where(row => row.CreatedAtUtc < afterCreatedAtUtc
                || (row.CreatedAtUtc == afterCreatedAtUtc && row.Id.CompareTo(afterId) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(PageSize + 1)
            .ToListAsync(cancellationToken);

        var amountAfter = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItemAmount, null, cancellationToken);
        if (!SameContext(context))
        {
            return Refused<FinanceOpenItemPageV1>(ReadOutcome.Denied);
        }
        var readAfter = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItems, null, cancellationToken);
        if (readAfter != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused<FinanceOpenItemPageV1>(ReadRefusal(readAfter));
        }

        var disclosure = Disclosure(amountBefore, amountAfter);
        var pageRows = rows.Take(PageSize).ToArray();
        var views = new FinanceOpenItemViewV1[pageRows.Length];
        for (var index = 0; index < pageRows.Length; index++)
        {
            if (!TryView(pageRows[index], disclosure, out views[index]))
            {
                return Refused<FinanceOpenItemPageV1>(ReadOutcome.Unavailable);
            }
        }

        var nextCursor = rows.Count > PageSize
            ? FinanceOpenItemReadCursor.Encode(
                context.CompanyId, context.ActorId, pageRows[^1].CreatedAtUtc, pageRows[^1].Id)
            : null;
        return new(ReadOutcome.Found, new FinanceOpenItemPageV1(context.CompanyId, views, nextCursor));
    }

    public async Task<ReadResult<FinanceOpenItemViewV1>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid openItemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused<FinanceOpenItemViewV1>(ReadOutcome.Denied);
        }

        var readBefore = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItems, openItemId, cancellationToken);
        if (readBefore != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused<FinanceOpenItemViewV1>(ReadRefusal(readBefore));
        }
        if (openItemId == Guid.Empty)
        {
            return Refused<FinanceOpenItemViewV1>(ReadOutcome.NotFound);
        }

        var amountBefore = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItemAmount, openItemId, cancellationToken);
        if (!SameContext(context))
        {
            return Refused<FinanceOpenItemViewV1>(ReadOutcome.Denied);
        }

        var row = await dbContext.OpenItems.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.CompanyId == context.CompanyId
                && candidate.Id == openItemId, cancellationToken);

        var amountAfter = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItemAmount, openItemId, cancellationToken);
        if (!SameContext(context))
        {
            return Refused<FinanceOpenItemViewV1>(ReadOutcome.Denied);
        }
        var readAfter = await permissions.CheckAsync(
            context, FinanceAction.ReadOpenItems, openItemId, cancellationToken);
        if (readAfter != PermissionDecision.Allowed || !SameContext(context))
        {
            return Refused<FinanceOpenItemViewV1>(ReadRefusal(readAfter));
        }

        if (row is null)
        {
            return Refused<FinanceOpenItemViewV1>(ReadOutcome.NotFound);
        }
        return TryView(row, Disclosure(amountBefore, amountAfter), out var view)
            ? new(ReadOutcome.Found, view)
            : Refused<FinanceOpenItemViewV1>(ReadOutcome.Unavailable);
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);

    private static bool TryView(
        FinanceOpenItemRow row,
        AmountDisclosure disclosure,
        out FinanceOpenItemViewV1 view)
    {
        view = null!;
        if (row.Id == Guid.Empty || row.CompanyId == Guid.Empty || row.SourceId == Guid.Empty
            || row.SourceLineId == Guid.Empty || row.PartyId == Guid.Empty || row.CompanyAccountId == Guid.Empty
            || row.CurrencyId == Guid.Empty || row.Version <= 0 || row.CreatedAtUtc == default
            || row.CreatedAtUtc.Offset != TimeSpan.Zero || !Canonical(row.SourceKind) || !Canonical(row.SourceVersion)
            || !FinanceExactDecimalText.TryParsePositive(row.OriginalAmountText, out var initial)
            || !FinanceExactDecimalText.TryParseNonNegative(row.OutstandingAmountText, out var outstanding)
            || outstanding.CompareTo(initial) > 0
            || !TryStatus(row.Status, out var status))
        {
            return false;
        }

        view = new FinanceOpenItemViewV1(
            row.Id,
            row.CompanyId,
            row.SourceKind,
            row.SourceId,
            row.SourceLineId,
            row.SourceVersion,
            row.PartyId,
            row.CompanyAccountId,
            row.CurrencyId,
            row.Version.ToString(CultureInfo.InvariantCulture),
            row.DueOn,
            disclosure == AmountDisclosure.Visible ? status : null,
            row.CreatedAtUtc,
            Amount(disclosure, row.OriginalAmountText, row.CurrencyId),
            Amount(disclosure, row.OutstandingAmountText, row.CurrencyId));
        return true;
    }

    private static bool TryStatus(int stored, out FinanceOpenItemStatusV1 status)
    {
        status = stored switch
        {
            FinanceOpenItemStatuses.Open => FinanceOpenItemStatusV1.Open,
            FinanceOpenItemStatuses.Closed => FinanceOpenItemStatusV1.Closed,
            _ => default,
        };
        return status != default;
    }

    private static bool Canonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static AmountView Amount(AmountDisclosure disclosure, string text, Guid currencyId) =>
        new(disclosure, disclosure == AmountDisclosure.Visible ? new MoneyText(text, currencyId) : null);

    private static AmountDisclosure Disclosure(PermissionDecision before, PermissionDecision after) =>
        before == PermissionDecision.Allowed && after == PermissionDecision.Allowed
            ? AmountDisclosure.Visible
            : before == PermissionDecision.Unavailable || after == PermissionDecision.Unavailable
                ? AmountDisclosure.Unavailable
                : AmountDisclosure.Redacted;

    private static ReadOutcome ReadRefusal(PermissionDecision decision) =>
        decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied;

    private static ReadResult<T> Refused<T>(ReadOutcome outcome) where T : class => new(outcome, null);
}
