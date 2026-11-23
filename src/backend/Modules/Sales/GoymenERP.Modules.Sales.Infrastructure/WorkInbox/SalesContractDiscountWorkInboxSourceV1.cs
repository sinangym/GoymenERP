using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4: the sales contracts whose discount waits for approval (S-11, NS-13, SAT-D346) for the Work inbox. A contract whose
/// discount needs approval is born PendingDiscountApproval and waits from its recording; the decision
/// (<c>sales.contracts.discount.approve</c>) accepts exactly that state and refuses the contract's creator (D4: the maker). Why it
/// waits is the owner's own comparison (<see cref="SalesContract.RequiresDiscountApproval"/>) against the limit snapshotted at
/// recording: one rule per discount rule above the limit (threshold = limit, actual = rate, deviation = rate − limit, in %), or
/// one "limit undefined" rule when no limit was configured (every discount goes to approval, SAT-D346). The rates and the limit
/// are withheld from a caller without the price read (SAT-D361/D363: the contract read withholds them too); the rule then
/// carries no numbers.
/// </summary>
internal sealed class SalesContractDiscountWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.contract-discount.v1";
    public const string OverLimitRule = "sales.contract-discount-over-limit";
    public const string LimitUndefinedRule = "sales.contract-discount-limit-undefined";
    private const string PercentUnit = "%";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.ContractsDiscountApprove;

    protected override bool CarriesPriceFigures => true;

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListPendingDiscountContractsAsync(companyId, take + 1, cancellationToken), take,
            disclosePrices);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxContractV1> contracts, int take, bool disclosePrices)
    {
        var items = new List<WorkInboxItemV1>(contracts.Count);
        foreach (var contract in contracts)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, contract.CompanyId);
            if (contract.Id == Guid.Empty || contract.ContractNo < 1
                || !SalesContract.RequiresDiscountApproval(contract.DiscountRules, contract.DiscountLimitPercent))
            {
                // A pending contract that its own comparison would not send to approval is an inconsistent row: fail closed.
                throw new InvalidOperationException("Kayıtlı sözleşmenin iskonto onayı olguları tutarsız.");
            }

            var label = SalesWorkInboxItemsV1.RequireLabel(
                $"Sözleşme #{contract.ContractNo.ToString(CultureInfo.InvariantCulture)}");
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                contract.Id,
                label,
                contract.CreatedAtUtc.ToUniversalTime(),
                SalesWorkInboxItemsV1.ContractsLink,
                SalesWorkInboxItemsV1.RequireActor(contract.CreatedBy),
                Rules(contract, disclosePrices),
                [new WorkApprovalReferenceV1(SalesWorkInboxItemsV1.ContractRole, label, contract.Id, null)]));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }

    private static IReadOnlyList<WorkApprovalRuleResultV1> Rules(SalesWorkInboxContractV1 contract, bool disclosePrices)
    {
        if (contract.DiscountLimitPercent is not { } limit)
        {
            return [new(LimitUndefinedRule, null, null, null, null)];
        }

        return [.. contract.DiscountRules
            .Where(rule => rule.RatePercent > limit)
            .OrderBy(rule => rule.RuleNo)
            .Take(WorkInboxRulesV1.MaxRulesPerItem)
            .Select(rule => disclosePrices
                ? new WorkApprovalRuleResultV1(OverLimitRule,
                    WorkInboxRulesV1.Decimal(limit),
                    WorkInboxRulesV1.Decimal(rule.RatePercent),
                    WorkInboxRulesV1.Decimal(rule.RatePercent - limit),
                    PercentUnit)
                : new WorkApprovalRuleResultV1(OverLimitRule, null, null, null, null))];
    }
}
