using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService : ISalesDiscountDecisionReadV1
{
    public async Task<SalesDiscountDecisionReadResultV1> ReadDiscountDecisionAsync(Guid draftId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        Scope? scope = null;
        var permission = bindings.DiscountExceptionApprovePermission;
        try
        {
            scope = await RequireAsync(permission, cancellationToken);
            await Fence(cancellationToken);
            if (draftId == Guid.Empty) return new(SalesDiscountDecisionReadStateV1.NotFound, null);
            if (store is not ISalesDiscountDecisionReadStoreV1 reader) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            var source = await reader.ReadDiscountDecisionSourceAsync(draftId, Fence, cancellationToken);
            await Fence(cancellationToken);
            if (source is null) return new(SalesDiscountDecisionReadStateV1.NotFound, null);
            if (source.Lines is null) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            source = source with { Lines = Array.AsReadOnly(source.Lines.ToArray()) };
            if (!ValidDiscountSource(source, scope.CompanyId, draftId)) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            var pending = source.Lines.Where(x => SalesOrderDraft.NeedsDiscountExceptionApproval(x.Commercial)
                && x.Commercial.DiscountCheck?.ApprovedAtUtc is null).ToArray();
            if (pending.Any(x => x.Commercial.DiscountCheck is null)) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            // SAT-D37/D45: every LISTED discount writer is excluded; unlisted self-approval remains native.
            if (pending.Any(x => x.Commercial.Reference is not null && x.Commercial.DiscountCheck!.CheckedBy == scope.ActorId))
                return new(SalesDiscountDecisionReadStateV1.Denied, null);
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
            return new(SalesDiscountDecisionReadStateV1.Found, ProjectDiscountDecision(source, disclosure));
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException failure) when (failure.Status is 401 or 403)
        { return new(SalesDiscountDecisionReadStateV1.Denied, null); }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or System.Text.Json.JsonException
            or FormatException or OverflowException or TimeoutException or NotSupportedException or System.Data.Common.DbException or SalesRuleException
            or SalesDraftServiceException or SalesDraftStoreException)
        {
            if (scope is not null)
            {
                try { await Fence(cancellationToken); }
                catch (SalesDraftServiceException denial) when (denial.Status is 401 or 403)
                { return new(SalesDiscountDecisionReadStateV1.Denied, null); }
                catch (OperationCanceledException) { throw; }
                catch (Exception recheck) when (recheck is SalesDraftServiceException or SalesDraftStoreException
                    or InvalidOperationException or TimeoutException or NotSupportedException or System.Data.Common.DbException)
                { return new(SalesDiscountDecisionReadStateV1.Unavailable, null); }
            }
            return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
        }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope!, permission, token);
            await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            scope = await RequireStillAsync(scope, permission, token); token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>The existing selected-owner projection, also used for exact source/receipt binding; no IO or grant.</summary>
    public static SalesDiscountDecisionDetailV1 ProjectDiscountDecision(SalesDiscountDecisionSourceV1 source, SalesPriceDisclosure disclosure)
    {
        var below = source.Lines.Where(x => SalesOrderDraft.NeedsDiscountExceptionApproval(x.Commercial)).ToArray();
        var detail = new SalesDiscountDecisionDetailV1(source.CompanyId, source.DraftId, source.DocumentNo,
            source.Version.ToString(CultureInfo.InvariantCulture), source.Revision, source.State, source.ApprovalState,
            source.PartyId, source.CompanyAccountId, source.CurrencyId, source.BusinessDate, source.IsSubcontractService,
            Array.AsReadOnly(source.Lines.Select(x => new SalesDiscountDecisionLineV1(x.LineId, x.LineNo, x.ProductId, x.ProductVersion,
                x.ProductCode, x.ProductName, x.OrderUnitId, x.Quantity,
                x.Commercial.DiscountCheck is { } check ? new(check.CheckedBy, check.CheckedAtUtc,
                    disclosure == SalesPriceDisclosure.Visible ? check.ListUnitPrice : null, check.LimitPercent, check.RequiresApproval,
                    check.ApprovedBy, check.ApprovedAtUtc, check.Reason) : null,
                disclosure == SalesPriceDisclosure.Visible ? Pricing(x.LineId, x.LineNo, x.Commercial) : null)).ToArray()),
            disclosure, !source.IsSubcontractService && source.Version < long.MaxValue && source.State == "Draft" && source.ApprovalState is "Preparing" or "Rejected"
                && below.Length > 0 && below.All(x => x.Commercial.DiscountCheck is { IsApproved: false }),
            SalesDiscountDecisionDigestsV1.SourceFactsDigest(source), "") { Source = source };
        return detail with { PreviewDigest = SalesDiscountDecisionDigestsV1.PreviewDigest(detail) };
    }

    public static bool MatchesDiscountDecisionDetail(SalesDiscountDecisionDetailV1? detail)
    {
        try
        {
            return detail?.Source is { } source && detail.PriceDisclosure is SalesPriceDisclosure.Visible or SalesPriceDisclosure.Withheld
                && ValidDiscountSource(source, detail.CompanyId, detail.DraftId)
                && System.Text.Json.JsonSerializer.Serialize(detail, SalesDraftJson.Options)
                    == System.Text.Json.JsonSerializer.Serialize(ProjectDiscountDecision(source, detail.PriceDisclosure), SalesDraftJson.Options);
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException or OverflowException or SalesRuleException)
        { return false; }
    }

    private static bool ValidDiscountSource(SalesDiscountDecisionSourceV1 source, Guid company, Guid id) =>
        source.CompanyId == company && source.DraftId == id && source.Version > 0 && source.Revision >= 0
        && !string.IsNullOrWhiteSpace(source.DocumentNo) && source.PartyId != Guid.Empty && source.CompanyAccountId != Guid.Empty
        && source.CurrencyId != Guid.Empty && source.State is "Draft" or "Cancelled" or "Closed"
        && source.ApprovalState is "Preparing" or "PendingApproval" or "Approved" or "Rejected" or "Withdrawn"
        && source.Lines is { Count: > 0 and <= SalesOrderDraft.MaximumLines }
        && source.Lines.All(x => x is not null && x.LineId != Guid.Empty && x.LineNo > 0 && x.ProductId != Guid.Empty
            && !string.IsNullOrWhiteSpace(x.ProductVersion) && !string.IsNullOrWhiteSpace(x.ProductCode)
            && !string.IsNullOrWhiteSpace(x.ProductName) && !string.IsNullOrWhiteSpace(x.Quantity)
            && x.OrderUnitId != Guid.Empty && x.Commercial is not null
            && (!SalesOrderDraft.NeedsDiscountExceptionApproval(x.Commercial) || x.Commercial.DiscountCheck is not null)
            && SalesDiscountDecisionDigestsV1.MatchesCommercialSnapshot(x.Commercial))
        && source.Lines.Select(x => x.LineId).Distinct().Count() == source.Lines.Count
        && source.Lines.Select(x => x.LineNo).Distinct().Count() == source.Lines.Count;
}
