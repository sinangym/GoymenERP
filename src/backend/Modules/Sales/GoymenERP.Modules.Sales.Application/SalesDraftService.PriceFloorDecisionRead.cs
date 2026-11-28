using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : ISalesPriceFloorDecisionReadV1
{
    public async Task<SalesPriceFloorDecisionReadResultV1> ReadPriceFloorDecisionAsync(Guid draftId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        Scope? scope = null;
        var permission = bindings.PriceFloorExceptionApprovePermission;
        try
        {
            scope = await RequireAsync(permission, cancellationToken);
            await Fence(cancellationToken);
            if (draftId == Guid.Empty) return new(SalesPriceFloorDecisionReadStateV1.NotFound, null);
            if (store is not ISalesPriceFloorDecisionReadStoreV1 reader) return new(SalesPriceFloorDecisionReadStateV1.Unavailable, null);
            var source = await reader.ReadPriceFloorDecisionSourceAsync(draftId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (source is null) return new(SalesPriceFloorDecisionReadStateV1.NotFound, null);
            if (!ValidPriceFloorSource(source, scope.CompanyId, draftId)) return new(SalesPriceFloorDecisionReadStateV1.Unavailable, null);
            var pending = source.Lines.Where(x => SalesOrderDraft.IsBelowPriceFloor(x.Commercial)
                && x.Commercial.FloorException?.ApprovedAtUtc is null).ToArray();
            // SAT-D1: every native requester participates in SoD, not just the draft's creator or first line.
            if (pending.Any(x => x.Commercial.FloorException!.RequestedBy == scope.ActorId))
                return new(SalesPriceFloorDecisionReadStateV1.Denied, null);
            var sourceDigest = SalesPriceFloorDecisionDigestsV1.SourceFactsDigest(source);
            await Fence(cancellationToken);
            var disclosure = await DiscloseAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope, permission, cancellationToken);
            // Re-measure after the consumer and the last approval-authority await. A removed PriceRead cannot release numbers.
            disclosure = await DiscloseAsync(scope, cancellationToken) == SalesPriceDisclosure.Visible
                && disclosure == SalesPriceDisclosure.Visible ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld;
            cancellationToken.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope, permission, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var released = await ReplayDisclosureAsync(scope, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            disclosure = released.Disclosure == SalesPriceDisclosure.Visible && disclosure == SalesPriceDisclosure.Visible
                ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld;
            var detail = new SalesPriceFloorDecisionDetailV1(source.CompanyId, source.DraftId, source.DocumentNo,
                source.Version.ToString(CultureInfo.InvariantCulture), source.Revision, source.State, source.ApprovalState,
                source.PartyId, source.CompanyAccountId, source.CurrencyId, source.BusinessDate,
                source.Lines.Select(x => new SalesPriceFloorDecisionLineV1(x.LineId, x.LineNo, x.ProductId, x.ProductVersion,
                    x.ProductCode, x.ProductName, x.OrderUnitId, x.Quantity,
                    x.Commercial.FloorException is { } f ? new(f.RequestedBy, f.RequestedAtUtc, f.ApprovedBy, f.ApprovedAtUtc, f.Reason) : null,
                    disclosure == SalesPriceDisclosure.Visible ? Pricing(x.LineId, x.LineNo, x.Commercial) : null)).ToArray(),
                disclosure, source.Version < long.MaxValue && source.State == "Draft"
                    && source.ApprovalState is "Preparing" or "Rejected" && pending.Length > 0
                    && pending.Length == source.Lines.Count(x => SalesOrderDraft.IsBelowPriceFloor(x.Commercial)),
                sourceDigest, "");
            return new(SalesPriceFloorDecisionReadStateV1.Found,
                detail with { PreviewDigest = SalesPriceFloorDecisionDigestsV1.PreviewDigest(detail) });
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
        { return new(SalesPriceFloorDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or System.Text.Json.JsonException
            or FormatException or OverflowException or TimeoutException or NotSupportedException or System.Data.Common.DbException or SalesRuleException
            or SalesDraftServiceException or SalesDraftStoreException)
        {
            if (scope is not null)
            {
                try { await Fence(cancellationToken); }
                catch (SalesDraftServiceException denial) when (denial.Status is 401 or 403)
                { return new(SalesPriceFloorDecisionReadStateV1.Denied, null); }
                catch (OperationCanceledException) { throw; }
                catch (Exception recheck) when (recheck is SalesDraftServiceException or SalesDraftStoreException
                    or InvalidOperationException or TimeoutException or NotSupportedException or System.Data.Common.DbException)
                { return new(SalesPriceFloorDecisionReadStateV1.Unavailable, null); }
            }
            return new(SalesPriceFloorDecisionReadStateV1.Unavailable, null);
        }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope!, permission, token);
            await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
        }
    }

    private static bool ValidPriceFloorSource(SalesPriceFloorDecisionSourceV1 source, Guid company, Guid id) =>
        source.CompanyId == company && source.DraftId == id && source.Version > 0 && source.Revision >= 0
        && !string.IsNullOrWhiteSpace(source.DocumentNo) && source.PartyId != Guid.Empty && source.CompanyAccountId != Guid.Empty
        && source.CurrencyId != Guid.Empty && source.State is "Draft" or "Cancelled" or "Closed"
        && source.ApprovalState is "Preparing" or "PendingApproval" or "Approved" or "Rejected" or "Withdrawn"
        && source.Lines is { Count: > 0 and <= SalesOrderDraft.MaximumLines }
        && source.Lines.All(x => x is not null && x.LineId != Guid.Empty && x.LineNo > 0 && x.ProductId != Guid.Empty
            && !string.IsNullOrWhiteSpace(x.ProductVersion) && !string.IsNullOrWhiteSpace(x.ProductCode)
            && !string.IsNullOrWhiteSpace(x.ProductName) && !string.IsNullOrWhiteSpace(x.Quantity)
            && x.OrderUnitId != Guid.Empty && x.Commercial is not null
            && SalesPriceFloorDecisionDigestsV1.MatchesCommercialSnapshot(x.Commercial))
        && source.Lines.Select(x => x.LineId).Distinct().Count() == source.Lines.Count
        && source.Lines.Select(x => x.LineNo).Distinct().Count() == source.Lines.Count;
}
