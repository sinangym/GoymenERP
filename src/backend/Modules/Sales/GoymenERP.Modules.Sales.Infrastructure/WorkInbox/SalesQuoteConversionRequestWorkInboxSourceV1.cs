using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>
/// WP4 (SAT-D476): the salespeople's conversion requests for expired quotes, for the Work inbox. Only a request waits — an expired
/// quote without one is never counted — and only while it stands as asked (its quote is open on the asked revision without that
/// revision's approval). It is decided on the quote screen by the quote's own expired-conversion approval
/// (<c>sales.quotes.expired-conversion.approve</c>), which SAT-D44 (2) opens to the quote's creator too: the owner names no maker
/// ≠ checker, so the item names no maker and no excluded decider. The rule carries no numbers.
/// </summary>
internal sealed class SalesQuoteConversionRequestWorkInboxSourceV1(
    ISalesWorkInboxApprovalStoreV1 store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : SalesWorkInboxSourceBaseV1(companyContext, currentUser, authority)
{
    public const string KindName = "sales.quote-conversion-request.v1";
    public const string RequestedRule = "sales.quote-expired-conversion-requested";

    public override string Kind => KindName;

    public override string ApproverPermission => SalesPermissionManifestV1.QuotesExpiredConversionApprove;

    protected override async Task<(IReadOnlyList<WorkInboxItemV1> Items, bool HasMore)> ReadAsync(Guid companyId, int take,
        bool disclosePrices, CancellationToken cancellationToken) =>
        Project(companyId, await store.ListPendingQuoteConversionRequestsAsync(companyId, take + 1, cancellationToken), take);

    internal static (IReadOnlyList<WorkInboxItemV1> Items, bool HasMore) Project(Guid companyId,
        IReadOnlyList<SalesWorkInboxQuoteConversionRequestV1> requests, int take)
    {
        var items = new List<WorkInboxItemV1>(requests.Count);
        foreach (var request in requests)
        {
            SalesWorkInboxItemsV1.RequireCompany(companyId, request.CompanyId);
            if (request.Id == Guid.Empty || request.QuoteId == Guid.Empty || request.RequestNo < 1)
            {
                throw new InvalidOperationException("Kayıtlı teklif dönüşüm talebi kimliği eksik.");
            }

            var documentNo = SalesWorkInboxItemsV1.RequireLabel(request.QuoteDocumentNo);
            var label = SalesWorkInboxItemsV1.RequireLabel(
                $"{documentNo} #{request.RequestNo.ToString(CultureInfo.InvariantCulture)}");
            var link = SalesWorkInboxItemsV1.QuoteLink(request.QuoteId);
            SalesWorkInboxItemsV1.RequireActor(request.RequestedBy);
            items.Add(new WorkInboxItemV1(
                SalesWorkInboxSourceBaseV1.ModuleName,
                KindName,
                request.Id,
                label,
                request.RequestedAtUtc.ToUniversalTime(),
                link,
                PreparedBy: null,
                [new WorkApprovalRuleResultV1(RequestedRule, null, null, null, null)],
                [new WorkApprovalReferenceV1(SalesWorkInboxItemsV1.QuoteRole, documentNo, request.QuoteId, link)]));
        }

        return SalesWorkInboxItemsV1.Page(items, take);
    }
}
