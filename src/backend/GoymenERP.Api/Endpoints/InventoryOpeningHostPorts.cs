using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Adapts Cari's existing authorized Manufacturing party-account source to Inventory's opening-owner seam.
/// The source owns disclosure and account facts; Inventory owns whether those facts are eligible for an opening.
/// This host preserves the source outcomes and fences Cari's company-wide read authority and the same actor/company
/// before and after the awaited read. A source fault is disclosed as unavailable only while that authority remains fresh.
/// </summary>
public sealed class CounterpartyInventoryOpeningOwnerAccountPortV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICounterpartyManufacturingPartyAccountSourceV1 source)
    : IInventoryOpeningOwnerAccountPortV1
{
    public async Task<InventoryOpeningOwnerAccountAnswerV1> GetAuthorizedAsync(
        Guid partyId,
        Guid companyPartyAccountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyPartyAccountId == Guid.Empty)
        {
            return Answer(InventoryOpeningOwnerAccountOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        if (!await HasFreshReadAsync(companyId, actorId, cancellationToken))
        {
            return Answer(InventoryOpeningOwnerAccountOutcomeV1.Denied);
        }

        CounterpartyManufacturingPartyAccountAnswerV1 answer;
        try
        {
            answer = await source.GetAuthorizedAsync(partyId, companyPartyAccountId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await HasFreshReadAsync(companyId, actorId, cancellationToken)
                ? Answer(InventoryOpeningOwnerAccountOutcomeV1.Unavailable)
                : Answer(InventoryOpeningOwnerAccountOutcomeV1.Denied);
        }

        if (!await HasFreshReadAsync(companyId, actorId, cancellationToken))
        {
            return Answer(InventoryOpeningOwnerAccountOutcomeV1.Denied);
        }

        return answer.Outcome switch
        {
            CounterpartyManufacturingPartyAccountOutcomeV1.Denied =>
                Answer(InventoryOpeningOwnerAccountOutcomeV1.Denied),
            CounterpartyManufacturingPartyAccountOutcomeV1.NotFound =>
                Answer(InventoryOpeningOwnerAccountOutcomeV1.NotFound),
            CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable =>
                Answer(InventoryOpeningOwnerAccountOutcomeV1.Unavailable),
            CounterpartyManufacturingPartyAccountOutcomeV1.Found when answer.Account is { } account =>
                new InventoryOpeningOwnerAccountAnswerV1(
                    InventoryOpeningOwnerAccountOutcomeV1.Found,
                    new InventoryOpeningOwnerAccountV1(
                        account.PartyId,
                        account.CompanyAccountId,
                        account.CompanyId,
                        account.PartyIsActive,
                        account.AccountIsActive,
                        account.HasCustomerRole,
                        account.PartyVersion,
                        account.AccountVersion)),
            _ => Answer(InventoryOpeningOwnerAccountOutcomeV1.Unavailable),
        };
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId);

    private bool RemainsCurrent(Guid companyId, string actorId) =>
        IsCurrent()
        && companyContext.CompanyId == companyId
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private async Task<bool> HasFreshReadAsync(
        Guid companyId,
        string actorId,
        CancellationToken cancellationToken)
    {
        if (!RemainsCurrent(companyId, actorId))
        {
            return false;
        }

        try
        {
            var measured = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.CounterpartyPartiesRead,
                companyId,
                cancellationToken);
            return measured.IsGranted && measured.IsCompanyWide && RemainsCurrent(companyId, actorId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static InventoryOpeningOwnerAccountAnswerV1 Answer(InventoryOpeningOwnerAccountOutcomeV1 outcome) =>
        new(outcome, null);
}

/// <summary>
/// INV-OPEN-1 part 2: the opening command's location port over Organization's module read
/// (<see cref="IOrganizationLocationStatusReaderV1"/>, #339). It is company-scoped and carries no user
/// permission, so a caller's missing Organization read grant can never look like a missing location.
/// The mapping is Organization's facts under the Inventory owner's precedence (#66 5741317520): no row
/// in the resolved company is <see cref="InventoryLocationStatus.NotFound"/>, then what the place is
/// (<c>IsExternal</c>, SINAN_KARARLARI_2026-08-31 §3) before its own state (<c>IsActive</c>). No site
/// cascade is decided anywhere, so none is applied. When Organization cannot answer it throws, and the
/// command answers 503: nothing here guesses "active".
/// </summary>
public sealed class OrganizationInventoryLocationStatusPort(IOrganizationLocationStatusReaderV1 locations)
    : IInventoryLocationStatusPort
{
    public async Task<InventoryLocationStatus> ReadAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = await locations.ReadAsync(companyId, locationId, cancellationToken);
        if (location is null)
        {
            return InventoryLocationStatus.NotFound;
        }

        if (location.IsExternal)
        {
            return InventoryLocationStatus.External;
        }

        return location.IsActive ? InventoryLocationStatus.Active : InventoryLocationStatus.Inactive;
    }
}

/// <summary>
/// INV-OPEN-1 part 2: the opening command's product port over Product's stock eligibility
/// (<see cref="IProductStockEligibilityV1"/>) for exactly the card version the counter read, on the
/// counted-on date. Product's refusals are answers: its field errors are returned relative to the count
/// line with only one rename, <c>expectedProductVersion</c> to the request's <c>productVersion</c>;
/// every other field and code is Product's own (the Sales precedent). Product's caller refusals keep
/// their meaning: 401 is the opening's <c>unauthenticated</c> and 403 its <c>forbidden</c>, never an
/// ineligible product or an unavailable owner (the opening user also needs
/// <c>product.operational-selection.read</c>, a human grant pairing). Every other failure throws, and
/// the command answers 503.
/// </summary>
public sealed class ProductInventoryStockPort(IProductStockEligibilityV1 eligibility) : IInventoryProductStockPort
{
    public async Task<InventoryProductStockAnswer> EvaluateAsync(Guid productId, string productVersion, DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        ProductStockEligibilityV1 answer;
        try
        {
            answer = await eligibility.EvaluateAsync(new ProductStockSelectionV1(productId, productVersion, effectiveOn),
                cancellationToken);
        }
        catch (ProductOperationException exception) when (exception.StatusCode == StatusCodes.Status401Unauthorized)
        {
            throw new InventoryOpeningServiceException(StatusCodes.Status401Unauthorized, InventoryOpeningCodesV1.Unauthenticated);
        }
        catch (ProductOperationException exception) when (exception.StatusCode == StatusCodes.Status403Forbidden)
        {
            throw new InventoryOpeningServiceException(StatusCodes.Status403Forbidden, InventoryOpeningCodesV1.Forbidden);
        }

        if (answer.Snapshot is not null)
        {
            return new InventoryProductStockAnswer(true, []);
        }

        return new InventoryProductStockAnswer(false, answer.Errors
            .Select(error => new InventoryFieldIssueV1(
                error.Field == "expectedProductVersion" ? "productVersion" : error.Field,
                error.Code))
            .ToArray());
    }
}
