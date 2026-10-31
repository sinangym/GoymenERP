using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// MOD-03: the tax master's read ports — the category reference read the Product owner checks a product's classes with, and
/// the line tax port the Sales order line / invoice consume (SAT-D73/D74/D93). Both read only Settings' own tables under
/// the company filter; the product's classes come from the Product owner through the host-composed
/// <see cref="ISettingsProductTaxClassSourceV1"/>. Access first (authenticated actor, member of the server company): else
/// <c>settings_access_denied</c>. An unreadable store or source is <c>settings_provider_unavailable</c>, never an empty or
/// zero answer. No permission is measured here: the answer is reference data and the consumer fences its own operation.
/// </summary>
internal sealed class SettingsTaxMasterReader(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ISettingsProductTaxClassSourceV1? productTaxClasses = null) : ISettingsTaxCategoryReaderV1, ISettingsLineTaxResolverV1
{
    public async Task<SettingsTaxCategoryV1?> FindAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (categoryId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var row = await database.TaxCategories.AsNoTracking()
                .SingleOrDefaultAsync(category => category.Id == categoryId, cancellationToken);
            return row is null ? null : new SettingsTaxCategoryV1(companyId, row.Id, row.Kind, row.Code, row.Name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }
    }

    public async Task<IReadOnlyList<SettingsTaxCategoryV1>> ListAsync(string kind, CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (!TaxCategoryKindsV1.IsKnown(kind))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        try
        {
            var rows = await database.TaxCategories.AsNoTracking()
                .Where(category => category.Kind == kind)
                .ToListAsync(cancellationToken);
            return rows.OrderBy(row => row.Code, StringComparer.Ordinal)
                .Select(row => new SettingsTaxCategoryV1(companyId, row.Id, row.Kind, row.Code, row.Name))
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }
    }

    public async Task<SettingsLineTaxResolutionV1> ResolveAsync(Guid productId, Guid? buyerCategoryId, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (productId == Guid.Empty || buyerCategoryId == Guid.Empty)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var source = productTaxClasses
            ?? throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        var classes = await source.GetAsync(companyId, productId, cancellationToken);
        if (classes.Outcome == SettingsProductTaxClassOutcomeV1.Denied)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
        }

        if (classes.Outcome is not (SettingsProductTaxClassOutcomeV1.Found or SettingsProductTaxClassOutcomeV1.NotFound
            or SettingsProductTaxClassOutcomeV1.NotClassified))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        var data = await ReadAsync(cancellationToken);
        if (classes.Outcome != SettingsProductTaxClassOutcomeV1.Found || classes.VatClassId is null)
        {
            var reason = classes.Outcome == SettingsProductTaxClassOutcomeV1.NotFound
                ? TaxResolutionReasonsV1.ProductNotFound
                : TaxResolutionReasonsV1.ProductNotClassified;
            return new SettingsLineTaxResolutionV1(companyId, productId, buyerCategoryId, on, null,
                TaxResolutionStatesV1.Unresolved, null, null, null, reason, null, TaxResolutionStatesV1.Unresolved, null, null,
                null, null, reason);
        }

        var parts = TaxMasterCalculatorV1.Resolve(data, classes.VatClassId, classes.WithholdingClassId, buyerCategoryId, on);
        return new SettingsLineTaxResolutionV1(
            companyId,
            productId,
            buyerCategoryId,
            on,
            classes.VatClassId,
            parts.VatState,
            parts.VatPercent,
            parts.VatRecord?.RecordId,
            parts.VatRecord?.RecordVersion,
            parts.VatReason,
            classes.WithholdingClassId,
            parts.WithholdingState,
            parts.WithholdingPercent,
            parts.WithholdingCode,
            parts.WithholdingRecord?.RecordId,
            parts.WithholdingRecord?.RecordVersion,
            parts.WithholdingReason);
    }

    public async Task<SettingsTaxCodeListResolutionV1> GetCodeListAsync(string kind, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (!TaxCodeListKindsV1.IsKnown(kind))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var data = await ReadAsync(cancellationToken);
        return TaxMasterCalculatorV1.ListInForce(data, kind, on) is { } list
            ? new SettingsTaxCodeListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                list.EffectiveFrom, list.Entries, null)
            : new SettingsTaxCodeListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                TaxResolutionReasonsV1.CodeListMissing);
    }

    private Guid RequireAccess()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
        }

        return companyContext.CompanyId;
    }

    private async Task<TaxMasterDataV1> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await TaxMasterRows.ReadAsync(database, trackCategoriesAndLists: false, cancellationToken)).ToData();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }
    }
}
