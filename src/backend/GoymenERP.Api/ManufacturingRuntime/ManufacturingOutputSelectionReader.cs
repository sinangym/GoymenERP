using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>
/// Maps Product's stock-eligible candidates and Organization's active internal locations into the
/// Manufacturing output selector. Manufacturing authority and request identity are fenced before
/// and after every owner read; owner contracts keep their own stricter authorization checks.
/// </summary>
internal sealed class ManufacturingOutputSelectionReader(
    IProductStockCandidatesV1 products,
    IOrganizationLocationCandidatesV1 locations,
    IErpAuthorityRevalidationService authority,
    ICompanyContext company,
    ICurrentUser user)
    : IManufacturingOutputSelectionV1
{
    public async Task<ManufacturingOutputProductCandidatesV1> SearchProductsAsync(
        ManufacturingOutputProductQueryV1 query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = await RequireScopeAsync(null, cancellationToken);
        ProductStockCandidatesV1 answer;
        try
        {
            answer = await products.SearchAsync(
                new ProductStockCandidateQueryV1(query.Q, query.EffectiveOn, query.Limit),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProductValidationException exception)
        {
            await RequireScopeAsync(scope, cancellationToken);
            throw Invalid(exception);
        }
        catch (ProductOperationException exception) when (exception.StatusCode is 401 or 403)
        {
            throw Denied(exception);
        }
        catch (Exception exception)
        {
            await RequireScopeAfterFailureAsync(scope, cancellationToken);
            throw Unavailable(exception);
        }

        await RequireScopeAsync(scope, cancellationToken);
        if (answer.Items.Any(item => item.ProductId == Guid.Empty
                                     || string.IsNullOrWhiteSpace(item.ProductVersion)
                                     || string.IsNullOrWhiteSpace(item.Code)
                                     || string.IsNullOrWhiteSpace(item.Name)
                                     || string.IsNullOrWhiteSpace(item.StockUnitCode))
            || answer.Items.Select(item => item.ProductId).Distinct().Count() != answer.Items.Count)
        {
            throw Unavailable();
        }

        return new ManufacturingOutputProductCandidatesV1(
            [.. answer.Items.Select(item => new ManufacturingOutputProductCandidateV1(
                item.ProductId,
                item.ProductVersion,
                item.Code,
                item.Name,
                item.StockUnitCode))],
            answer.SearchWindowFull);
    }

    public async Task<ManufacturingOutputLocationCandidatesV1> SearchLocationsAsync(
        ManufacturingOutputLocationQueryV1 query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = await RequireScopeAsync(null, cancellationToken);
        OrganizationLocationCandidatesV1 answer;
        try
        {
            answer = await locations.SearchAsync(
                scope.CompanyId,
                new OrganizationLocationCandidateQueryV1(query.Q, query.Limit),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OrganizationLocationCandidateQueryException exception)
        {
            await RequireScopeAsync(scope, cancellationToken);
            throw Invalid(exception);
        }
        catch (OrganizationCompanyContextMismatchException exception)
        {
            throw Denied(exception);
        }
        catch (Exception exception)
        {
            await RequireScopeAfterFailureAsync(scope, cancellationToken);
            throw Unavailable(exception);
        }

        await RequireScopeAsync(scope, cancellationToken);
        if (answer.Items.Any(item => item.LocationId == Guid.Empty
                                     || item.SiteId == Guid.Empty
                                     || string.IsNullOrWhiteSpace(item.LocationCode)
                                     || string.IsNullOrWhiteSpace(item.LocationName)
                                     || string.IsNullOrWhiteSpace(item.SiteCode)
                                     || string.IsNullOrWhiteSpace(item.SiteName))
            || answer.Items.Select(item => item.LocationId).Distinct().Count() != answer.Items.Count)
        {
            throw Unavailable();
        }

        return new ManufacturingOutputLocationCandidatesV1(
            [.. answer.Items.Select(item => new ManufacturingOutputLocationCandidateV1(
                item.LocationId,
                item.LocationCode,
                item.LocationName,
                item.SiteId,
                item.SiteCode,
                item.SiteName))],
            answer.SearchWindowFull);
    }

    private async Task<SelectionScope> RequireScopeAsync(
        SelectionScope? expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId)
            || !company.IsResolved
            || company.CompanyId == Guid.Empty
            || !user.CanAccessCompany(company.CompanyId))
        {
            throw Denied();
        }

        var current = new SelectionScope(company.CompanyId, user.UserId);
        if (expected is not null && expected != current)
        {
            throw Denied();
        }

        var grant = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingFactsRecord,
            current.CompanyId,
            cancellationToken);
        if (!grant.IsGranted || !grant.IsCompanyWide
            || !user.IsAuthenticated
            || !company.IsResolved
            || company.CompanyId != current.CompanyId
            || !string.Equals(user.UserId, current.ActorId, StringComparison.Ordinal)
            || !user.CanAccessCompany(current.CompanyId))
        {
            throw Denied();
        }

        return current;
    }

    private async Task RequireScopeAfterFailureAsync(SelectionScope scope, CancellationToken cancellationToken)
    {
        try
        {
            await RequireScopeAsync(scope, cancellationToken);
        }
        catch (ManufacturingOutputSelectionException exception)
            when (exception.Failure == ManufacturingOutputSelectionFailure.Denied)
        {
            throw;
        }
    }

    private static ManufacturingOutputSelectionException Invalid(Exception? inner = null) =>
        new(ManufacturingOutputSelectionFailure.Invalid, ManufacturingOutputSelectionCodesV1.InvalidQuery, inner);

    private static ManufacturingOutputSelectionException Denied(Exception? inner = null) =>
        new(ManufacturingOutputSelectionFailure.Denied, ManufacturingOutputSelectionCodesV1.Denied, inner);

    private static ManufacturingOutputSelectionException Unavailable(Exception? inner = null) =>
        new(ManufacturingOutputSelectionFailure.Unavailable, ManufacturingOutputSelectionCodesV1.Unavailable, inner);

    private sealed record SelectionScope(Guid CompanyId, string ActorId);
}
