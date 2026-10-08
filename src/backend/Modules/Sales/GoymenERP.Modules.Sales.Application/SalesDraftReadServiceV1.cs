using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// Enriches the existing Sales draft reads with the Cari owner's current party presentation.
/// The existing service remains the owner of tenant, draft, price and persona semantics. This
/// decorator adds a fresh Sales read fence around one bounded Cari batch and is deliberately
/// absent from production DI until central integration changes endpoint, adapter and OpenAPI
/// atomically.
/// </summary>
public sealed class SalesDraftReadServiceV1(
    ISalesDraftService drafts,
    ISalesPartyDisplaySourceV1 partyDisplays,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    SalesDraftServiceBindings bindings) : ISalesDraftReadServiceV1
{
    private const int MaximumBatchSize = 100;

    public async Task<SalesOrderDraftReadDetailViewV1> GetDraftDetailAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        var detail = await drafts.GetDraftDetailAsync(draftId, cancellationToken);
        var displays = await ReadPartyDisplaysAsync([detail.Draft.PartyId], detail.Draft.CompanyId, cancellationToken);
        var display = displays[detail.Draft.PartyId];

        return new SalesOrderDraftReadDetailViewV1(
            detail.Draft, detail.RequestedDeliveryDate, detail.Note, detail.Lines,
            detail.DeliveryAddress, detail.Contact, detail.Responsible, detail.PaymentTerms,
            detail.SourceStrategy, detail.Package, detail.Approval, detail.CustomerAcceptance,
            detail.IsOwnDraft, detail.IsOwnDecisionRestricted, display);
    }

    public async Task<SalesOrderDraftReadListResponseV1> GetDraftListAsync(
        SalesOrderDraftListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await drafts.GetDraftListAsync(query, cancellationToken);
        if (page.Items.Count == 0)
        {
            return new SalesOrderDraftReadListResponseV1([], page.NextCursor);
        }

        var companyId = page.Items[0].CompanyId;
        if (page.Items.Any(item => item.CompanyId != companyId))
        {
            throw Unavailable();
        }

        var displays = await ReadPartyDisplaysAsync(
            page.Items.Select(item => item.PartyId),
            companyId,
            cancellationToken);

        return new SalesOrderDraftReadListResponseV1(
            [.. page.Items.Select(item => ReadSummary(item, displays[item.PartyId]))],
            page.NextCursor);
    }

    private async Task<IReadOnlyDictionary<Guid, SalesPartyDisplayV1>> ReadPartyDisplaysAsync(
        IEnumerable<Guid> partyIds,
        Guid resultCompanyId,
        CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToArray();
        if (ids.Length is < 1 or > MaximumBatchSize || ids.Any(id => id == Guid.Empty))
        {
            throw Unavailable();
        }

        var scope = await RequireScopeAsync(cancellationToken);
        if (scope.CompanyId != resultCompanyId)
        {
            throw Unavailable();
        }

        SalesPartyDisplayBatchAnswerV1 answer;
        try
        {
            answer = await partyDisplays.GetAuthorizedBatchAsync(
                new SalesPartyDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, ids),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, cancellationToken);
            throw Unavailable();
        }

        await RequireStillAsync(scope, cancellationToken);

        if (answer is null)
        {
            throw Unavailable();
        }

        if (answer.Outcome == SalesPartyDisplayBatchOutcomeV1.Denied)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (answer.Outcome != SalesPartyDisplayBatchOutcomeV1.Success || answer.Items is null
            || answer.Items.Count != ids.Length)
        {
            throw Unavailable();
        }

        var requested = ids.ToHashSet();
        var byId = new Dictionary<Guid, SalesPartyDisplayV1>();
        foreach (var item in answer.Items)
        {
            if (item is null || item.PartyId == Guid.Empty || !requested.Contains(item.PartyId)
                || string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.LegalName)
                || item.TradeName is not null && string.IsNullOrWhiteSpace(item.TradeName)
                || !byId.TryAdd(item.PartyId, item))
            {
                throw Unavailable();
            }
        }

        return byId;
    }

    private async Task<ReadScope> RequireScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(bindings.ReadPermission) || !ErpPermissions.IsKnown(bindings.ReadPermission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var permission = await authority.RevalidatePermissionScopeAsync(
            bindings.ReadPermission,
            companyId,
            cancellationToken);
        if (!permission.IsGranted || !permission.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new ReadScope(companyId, actorId);
    }

    private async Task RequireStillAsync(ReadScope expected, CancellationToken cancellationToken)
    {
        var current = await RequireScopeAsync(cancellationToken);
        if (current != expected)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    private static SalesOrderDraftReadSummaryViewV1 ReadSummary(
        SalesOrderDraftSummaryView draft,
        SalesPartyDisplayV1 display) =>
        new(draft.Id, draft.CompanyId, draft.DocumentNo, draft.Kind, draft.State,
            draft.Version, draft.Revision, draft.PartyId, draft.CompanyAccountId,
            draft.CurrencyId, draft.BusinessDate, draft.CreatedAtUtc, draft.LineCount,
            draft.ApprovalState, draft.CommercialStage, draft.ProductSnapshots, display);

    private static SalesDraftServiceException Unavailable() =>
        new(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);

    private sealed record ReadScope(Guid CompanyId, string ActorId);
}
