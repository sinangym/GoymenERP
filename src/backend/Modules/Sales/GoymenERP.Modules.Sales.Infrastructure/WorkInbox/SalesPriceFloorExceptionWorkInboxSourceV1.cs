using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// W1-A: the below-floor price exceptions of order drafts (AT-021) for the Work inbox. One item per draft, as the approval
/// (<c>ApproveSalesOrderDraftPriceFloorExceptionsV1</c>, route id = the draft id) decides every current below-floor line at
/// once. A draft is listed exactly when that command accepts it: not cancelled, lines open (Preparing or Rejected), at least one
/// current line below its floor and every such line carrying a not-yet-approved exception snapshot. One rule per below-floor
/// line (at most <see cref="WorkInboxRulesV1.MaxRulesPerItem"/>, by line number) with the snapshot's floor and net unit price
/// and the shortfall (floor − net, the owner's exact calculator), in the price list's currency.
/// </summary>
internal sealed class SalesPriceFloorExceptionWorkInboxSourceV1(
    ISalesWorkInboxStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.price-floor-exception.v1";
    public const string BelowFloorRule = "sales.price-below-floor";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove;

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
            var belowFloor = draft.Lines
                .Where(line => SalesOrderDraft.IsBelowPriceFloor(line.Commercial))
                .OrderBy(line => line.LineNo)
                .ToArray();
            if (belowFloor.Length == 0
                || belowFloor.Any(line => line.Commercial.FloorException is not { IsApproved: false }))
            {
                // Nothing below the floor, or a line the approval cannot take as it stands (no snapshot / already approved).
                continue;
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(draft.DocumentNo);
            var first = belowFloor
                .OrderBy(line => line.Commercial.FloorException!.RequestedAtUtc)
                .ThenBy(line => line.LineNo)
                .First();
            var firstException = first.Commercial.FloorException!;
            var rules = belowFloor
                .Take(WorkInboxRulesV1.MaxRulesPerItem)
                .Select(line => Rule(line, disclosePrices))
                .ToArray();
            var references = SalesWorkInboxItemsV1.References(
                new[] { SalesWorkInboxItemsV1.OrderReference(draft.Id, documentNo) }
                    .Concat(belowFloor.Select(line => SalesWorkInboxItemsV1.Reference(SalesWorkInboxItemsV1.PriceListRole,
                        line.Commercial.Reference?.PriceListName, line.Commercial.Reference?.PriceListId)))
                    .Concat(belowFloor.Select(line => SalesWorkInboxItemsV1.Reference(SalesWorkInboxItemsV1.ProductRole,
                        SalesWorkInboxItemsV1.ProductLabel(line.ProductCode, line.ProductName), line.ProductId))));
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                draft.Id,
                documentNo,
                firstException.RequestedAtUtc.ToUniversalTime(),
                SalesWorkInboxItemsV1.OrderLink(draft.Id),
                SalesWorkInboxItemsV1.RequireActor(firstException.RequestedBy),
                rules,
                references));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }

    private static WorkApprovalRuleResultV1 Rule(SalesWorkInboxDraftLineV1 line, bool disclosePrices)
    {
        if (!disclosePrices)
        {
            return new(BelowFloorRule, null, null, null, null);
        }

        var exception = line.Commercial.FloorException!;
        var floor = SalesLinePriceCalculator.Parse(exception.FloorUnitPrice);
        var net = SalesLinePriceCalculator.Parse(exception.NetUnitPrice);
        var shortfall = SalesLinePriceCalculator.ExactSum([floor], [net]);
        return new(BelowFloorRule,
            WorkInboxRulesV1.Decimal(floor),
            WorkInboxRulesV1.Decimal(net),
            WorkInboxRulesV1.Decimal(shortfall),
            SalesWorkInboxItemsV1.CurrencyUnit(line.Commercial.Reference?.CurrencyCode));
    }
}
