using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// Enriches the existing Sales draft reads with the Cari owner's current party presentation and (MOD-08, detail only)
/// the Identity owner's display name of the people the detail already identifies by id.
/// The existing service remains the owner of tenant, draft, price and persona semantics. This
/// decorator adds a fresh Sales read fence around one bounded Cari batch and is deliberately
/// absent from production DI until central integration changes endpoint, adapter and OpenAPI
/// atomically.
/// </summary>
public sealed class SalesDraftReadServiceV1(
    ISalesDraftService drafts,
    ISalesPartyDisplaySourceV1 partyDisplays,
    ISalesUserDisplaySourceV1 userDisplays,
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
        var users = await ReadUserDisplaysAsync(detail, cancellationToken);

        return new SalesOrderDraftReadDetailViewV1(
            detail.Draft, detail.RequestedDeliveryDate, detail.Note, detail.Lines,
            detail.DeliveryAddress, detail.Contact, detail.Responsible, detail.PaymentTerms,
            detail.SourceStrategy, detail.Package, detail.Approval, detail.CustomerAcceptance,
            detail.IsOwnDraft, detail.IsOwnDecisionRestricted, display, users);
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

    /// <summary>
    /// MOD-08: the people the detail names (approval submitter and decider, acceptance recorder), resolved once each by
    /// the Identity owner in the record's company under the same fresh Sales read fence as the party display. An actor
    /// id that is not a canonical user id is not asked; a person the owner does not answer has no entry.
    /// </summary>
    private async Task<IReadOnlyList<SalesUserDisplayV1>> ReadUserDisplaysAsync(
        SalesOrderDraftDetailView detail,
        CancellationToken cancellationToken)
    {
        var ids = ActorUserIds(detail);
        if (ids.Length == 0)
        {
            return [];
        }

        var scope = await RequireScopeAsync(cancellationToken);
        if (scope.CompanyId != detail.Draft.CompanyId)
        {
            throw UserDirectoryUnavailable();
        }

        SalesUserDisplayBatchAnswerV1 answer;
        try
        {
            answer = await userDisplays.GetAuthorizedBatchAsync(
                new SalesUserDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, ids),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, cancellationToken);
            throw UserDirectoryUnavailable();
        }

        await RequireStillAsync(scope, cancellationToken);

        if (answer is null)
        {
            throw UserDirectoryUnavailable();
        }

        if (answer.Outcome == SalesUserDisplayBatchOutcomeV1.Denied)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (answer.Outcome != SalesUserDisplayBatchOutcomeV1.Success || answer.Items is null
            || answer.Items.Count > ids.Length)
        {
            throw UserDirectoryUnavailable();
        }

        var requested = ids.ToHashSet();
        var byId = new Dictionary<Guid, SalesUserDisplayV1>();
        foreach (var item in answer.Items)
        {
            if (item is null || !requested.Contains(item.UserId) || string.IsNullOrWhiteSpace(item.DisplayName)
                || !byId.TryAdd(item.UserId, item))
            {
                throw UserDirectoryUnavailable();
            }
        }

        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    private static Guid[] ActorUserIds(SalesOrderDraftDetailView detail) =>
        [.. new[] { detail.Approval.SubmittedBy, detail.Approval.DecidedBy, detail.CustomerAcceptance.AcceptedBy }
            .Select(actor => Guid.TryParseExact(actor, "D", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()];

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

    private static SalesDraftServiceException UserDirectoryUnavailable() =>
        new(503, SalesDraftProblemCodesV1.IdentityProviderUnavailable);

    private sealed record ReadScope(Guid CompanyId, string ActorId);
}
