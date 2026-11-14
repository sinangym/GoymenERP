using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-D42 (3) (SINAN_KARARLARI_2026-11-13): "Kural yeni kapanışlara uygulanır." An order closed before URT-D41 with
// output above input on a parent roll is not reopened, re-assessed or changed; it is listed in this read-only report.
// Where and in which shape the report lives was left to this slice; it reads the stored orders through the one
// mass-balance calculator the close uses and stores nothing.

/// <summary>
/// One page of the company's closed orders, the latest close first (closedAtUtc desc, id desc), each rebuilt from its
/// stored facts exactly as the order read rebuilds it (a close stored under an earlier rule replays unchanged).
/// <see cref="NextCursor"/> null is the end; the cursor is opaque and bound to the company and to this list's own
/// ordering (an order-list or plan-list cursor is not one of its cursors).
/// </summary>
public sealed record ManufacturingClosedOrderPage(IReadOnlyList<ProductionOrder> Orders, string? NextCursor);

/// <summary>
/// The closed orders of the server-resolved company, for the URT-D42 (3) report. A port of its own beside
/// <see cref="IManufacturingOrderStore"/>, so the order store's existing implementations are not widened (the plan-list
/// pattern); the one store implements it. It reads; it never changes an order.
/// </summary>
public interface IManufacturingClosedOrderListStore
{
    /// <summary>
    /// One page of this company's closed orders, the latest close first. <paramref name="cursor"/> is the previous page's
    /// <see cref="ManufacturingClosedOrderPage.NextCursor"/>; another company's, another ordering's or an unreadable cursor
    /// is <see cref="ManufacturingStoreCodes.ListCursorInvalid"/>. A page size outside the store's bound is an
    /// <see cref="ArgumentOutOfRangeException"/>. A stored order that does not rebuild is refused, never skipped.
    /// </summary>
    Task<ManufacturingClosedOrderPage> ListClosedOrdersAsync(int pageSize, string? cursor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One parent roll of a closed order on which more mass is accounted than was issued (BAL-003 below zero,
/// <see cref="MassBalance.OutputExceedsInput"/>): the order read's own BAL-003 answer for the roll — the actual input
/// (net issued), what the categories account for, and the signed (negative) difference.
/// </summary>
public sealed record OutputAboveInputRollViewV1(Guid ParentRollId, MassDifferenceViewV1 Difference);

/// <summary>
/// One closed order with output above input on at least one parent roll (URT-D41 (1), URT-D42 (1): per roll; rolls never
/// offset each other). Only those rolls are listed. <see cref="ClosedWithExceptionApproval"/> tells how the earlier rule
/// let it close: true with the reasoned out-of-tolerance approval, false inside the stored tolerance. The order itself is
/// not changed by being listed.
/// </summary>
public sealed record OutputAboveInputClosureViewV1(
    Guid OrderId, Guid PlanId, Guid WorkCenterId, string Process, long Version, Guid ClosedByUserId,
    DateTimeOffset ClosedAtUtc, bool ClosedWithExceptionApproval, IReadOnlyList<OutputAboveInputRollViewV1> Rolls);

/// <summary>
/// One page of the URT-D42 (3) report. The page walks the company's closed orders, the latest close first:
/// <see cref="ExaminedOrderCount"/> closed orders were examined for this page and <see cref="Closures"/> are those of them
/// with output above input, so a page may list none and still have a <see cref="NextCursor"/>. Null cursor is the end.
/// <see cref="FormulaVersion"/> is the mass-balance calculator's (GDC-BAL/1), the same one the close and the order read use.
/// </summary>
public sealed record OutputAboveInputClosureReportViewV1(
    string FormulaVersion, int ExaminedOrderCount, IReadOnlyList<OutputAboveInputClosureViewV1> Closures,
    string? NextCursor);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// URT-D42 (3): the company's already-closed orders whose output exceeded input on a parent roll, per roll, listed and
    /// never changed. The order read's rules: the server-resolved company only (another company's order is in no page),
    /// the order read permission (<see cref="ErpPermissions.ManufacturingOrdersRead"/>, measured again after the read), a
    /// page size of at most 100 closed orders examined, and a cursor of another company, another ordering or no grammar is
    /// a 400 on <c>cursor</c>, never a silent first page. Without the closed-order port the answer is a retryable 503,
    /// never an empty page. A stored order that does not rebuild fails the whole request as the order read and the order
    /// list do (the redacted internal error), so it is never skipped and no partial page is answered. The rule is
    /// <see cref="MassBalance.OutputExceedsInput"/>, the close's own definition.
    /// </summary>
    public async Task<ManufacturingApiResult<OutputAboveInputClosureReportViewV1>> ListOutputAboveInputClosuresAsync(
        int? pageSize, string? cursor, CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<OutputAboveInputClosureReportViewV1>();
        }

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize)
        {
            return Invalid<OutputAboveInputClosureReportViewV1>("pageSize");
        }

        if (cursor is { Length: 0 or > MaximumCursorLength })
        {
            return Invalid<OutputAboveInputClosureReportViewV1>("cursor");
        }

        if (closedOrders is null)
        {
            return new ManufacturingApiResult<OutputAboveInputClosureReportViewV1>(
                ManufacturingApiStatus.Unavailable, null, false, null);
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<OutputAboveInputClosureReportViewV1> answer;
        try
        {
            var page = await closedOrders.ListClosedOrdersAsync(size, cursor, cancellationToken);

            // A page holding anything but this company's closed orders breaks the port's contract: it fails closed with
            // nothing listed, never a partial page.
            answer = OutputAboveInputClosureReportView(page) is { } report
                ? new ManufacturingApiResult<OutputAboveInputClosureReportViewV1>(ManufacturingApiStatus.Ok, report,
                    false, null)
                : new ManufacturingApiResult<OutputAboveInputClosureReportViewV1>(
                    ManufacturingApiStatus.Unavailable, null, false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ListCursorInvalid)
        {
            answer = Invalid<OutputAboveInputClosureReportViewV1>("cursor");
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<OutputAboveInputClosureReportViewV1>(
                ManufacturingApiStatus.Unavailable, null, false, null);
        }

        // The same fresh measure as the mass-balance read: a grant withdrawn while the page was read answers nothing of it.
        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<OutputAboveInputClosureReportViewV1>();
    }

    /// <summary>
    /// The report page from the examined closed orders: an order is listed when at least one planned parent roll has
    /// output above input, and only those rolls are. The balance is <see cref="ProductionOrder.BalanceOf"/>, so the
    /// numbers are the mass-balance read's. Null when the page holds anything but this company's closed orders (another
    /// company's order, an open one, a close without its closer and moment): such a page is never reported, in part or
    /// whole.
    /// </summary>
    internal OutputAboveInputClosureReportViewV1? OutputAboveInputClosureReportView(ManufacturingClosedOrderPage page)
    {
        var closures = new List<OutputAboveInputClosureViewV1>();
        foreach (var order in page.Orders)
        {
            if (order.CompanyId != companyContext.CompanyId || order.Status != OrderStatus.Closed
                || order.ClosedByUserId is not { } closedBy || order.ClosedAtUtc is not { } closedAt)
            {
                return null;
            }

            List<OutputAboveInputRollViewV1> rolls = [];
            foreach (var input in order.PlannedInputs)
            {
                var balance = order.BalanceOf(input.ParentRollId);
                if (!balance.OutputExceedsInput)
                {
                    continue;
                }

                var difference = balance.Reconciliation;
                rolls.Add(new OutputAboveInputRollViewV1(input.ParentRollId,
                    new MassDifferenceViewV1(Text(balance.NetIssued.Value), Text(difference.AccountedKg),
                        Text(difference.DifferenceKg), difference.IsFullyAccounted)));
            }

            if (rolls.Count > 0)
            {
                closures.Add(new OutputAboveInputClosureViewV1(order.Id, order.PlanId, order.WorkCenterId,
                    order.Process.ToString(), order.Version, closedBy, closedAt, order.ClosureExceptionApproval is not null,
                    rolls));
            }
        }

        return new OutputAboveInputClosureReportViewV1(MassBalanceEngineV1.FormulaVersion, page.Orders.Count, closures,
            page.NextCursor);
    }
}
