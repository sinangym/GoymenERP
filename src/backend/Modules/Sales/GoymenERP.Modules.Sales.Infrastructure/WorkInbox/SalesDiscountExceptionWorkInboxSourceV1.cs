using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// W1-A: the over-limit discount exceptions of order drafts (SAT-D2 / API-12) for the Work inbox. One item per draft, as the
/// approval (<c>ApproveSalesOrderDraftDiscountExceptionsV1</c>, route id = the draft id) decides every current over-limit line
/// at once. A draft is listed exactly when that command accepts it: not cancelled, lines open (Preparing or Rejected), not a
/// subcontract service order (SAT-D195), at least one current line needing the approval and every such line carrying a
/// not-yet-approved check. One rule per such line (at most <see cref="WorkInboxRulesV1.MaxRulesPerItem"/>, by line number),
/// from the line's stored check only:
/// <list type="bullet">
/// <item><see cref="OverLimitRule"/>: measured against a list price under a defined limit — the limit percent the check
/// recorded; the requested discount percent and its excess over the limit only where the stored percent IS the measured
/// discount (the price is the list price and no discount amount is set) and the caller may read prices.</item>
/// <item><see cref="LimitUndefinedRule"/>: measured against a list price, no limit defined for the actor (SAT-D36).</item>
/// <item><see cref="WithoutListPriceRule"/>: no list price to measure against (SAT-D43); no numbers.</item>
/// </list>
/// </summary>
internal sealed class SalesDiscountExceptionWorkInboxSourceV1(
    ISalesWorkInboxStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.discount-exception.v1";
    public const string OverLimitRule = "sales.discount-over-limit";
    public const string LimitUndefinedRule = "sales.discount-limit-undefined";
    public const string WithoutListPriceRule = "sales.discount-without-list-price";
    private const string PercentUnit = "%";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.OrdersDiscountExceptionApprove;

    protected override bool CarriesPriceFigures => true;

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken)
    {
        var drafts = await store.ListOpenDraftsWithPricedLinesAsync(companyId, cancellationToken);
        return Project(companyId, drafts, take, disclosePrices);
    }

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxDraftV1> drafts, int take, bool disclosePrices)
    {
        var items = new List<WorkInboxItemV1>();
        foreach (var draft in drafts)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, draft.CompanyId);
            if (draft.IsSubcontractService)
            {
                continue;
            }

            var overLimit = draft.Lines
                .Where(line => SalesOrderDraft.NeedsDiscountExceptionApproval(line.Commercial))
                .OrderBy(line => line.LineNo)
                .ToArray();
            if (overLimit.Length == 0
                || overLimit.Any(line => line.Commercial.DiscountCheck is not { IsApproved: false }))
            {
                // Nothing over the limit, or a line the approval cannot take as it stands (unchecked / already approved).
                continue;
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(draft.DocumentNo);
            var first = overLimit
                .OrderBy(line => line.Commercial.DiscountCheck!.CheckedAtUtc)
                .ThenBy(line => line.LineNo)
                .First();
            var firstCheck = first.Commercial.DiscountCheck!;
            var rules = overLimit
                .Take(WorkInboxRulesV1.MaxRulesPerItem)
                .Select(line => Rule(line.Commercial.DiscountCheck!, disclosePrices))
                .ToArray();
            var references = SalesWorkInboxItemsV1.References(
                new[] { SalesWorkInboxItemsV1.OrderReference(draft.Id, documentNo) }
                    .Concat(overLimit.Select(line => SalesWorkInboxItemsV1.Reference(SalesWorkInboxItemsV1.PriceListRole,
                        line.Commercial.Reference?.PriceListName, line.Commercial.Reference?.PriceListId)))
                    .Concat(overLimit.Select(line => SalesWorkInboxItemsV1.Reference(SalesWorkInboxItemsV1.ProductRole,
                        SalesWorkInboxItemsV1.ProductLabel(line.ProductCode, line.ProductName), line.ProductId))));
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                draft.Id,
                documentNo,
                firstCheck.CheckedAtUtc.ToUniversalTime(),
                SalesWorkInboxItemsV1.OrderLink(draft.Id),
                SalesWorkInboxItemsV1.RequireActor(firstCheck.CheckedBy),
                rules,
                references));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }

    private static WorkApprovalRuleResultV1 Rule(DiscountLimitCheckSnapshot check, bool disclosePrices)
    {
        if (!check.IsMeasured)
        {
            return new(WithoutListPriceRule, null, null, null, null);
        }

        decimal? requested = disclosePrices
            && check.DiscountPercent is { } percent
            && check.DiscountAmount is null
            && check.ListUnitPrice is { } listPrice
            && SalesLinePriceCalculator.SameValue(check.UnitPrice, listPrice)
                ? SalesLinePriceCalculator.Parse(percent)
                : null;
        if (check.LimitPercent is not { } limitText)
        {
            return requested is { } undefinedActual
                ? new(LimitUndefinedRule, null, WorkInboxRulesV1.Decimal(undefinedActual), null, PercentUnit)
                : new(LimitUndefinedRule, null, null, null, null);
        }

        var limit = SalesLinePriceCalculator.Parse(limitText);
        var excess = requested is { } actual && actual > limit
            ? SalesLinePriceCalculator.ExactSum([actual], [limit])
            : (decimal?)null;
        return new(OverLimitRule,
            WorkInboxRulesV1.Decimal(limit),
            requested is { } shown ? WorkInboxRulesV1.Decimal(shown) : null,
            excess is { } over ? WorkInboxRulesV1.Decimal(over) : null,
            PercentUnit);
    }
}
