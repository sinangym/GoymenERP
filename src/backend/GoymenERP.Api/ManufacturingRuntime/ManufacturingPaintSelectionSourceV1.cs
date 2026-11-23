using System.Globalization;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>URT-D25: read-only composition of actual Product membership, the existing authorized Cari supplier
/// adapter and Settings' PaintColor owner. It never writes another module or infers a membership from text.</summary>
public sealed class ManufacturingPaintSelectionSourceV1(ICompanyContext company, ICurrentUser user,
    IProductPaintMembershipReaderV1 products, IPurchasingPartyAccountPortV1 suppliers,
    ICounterpartyReadService counterparties, IActorSessionBindingResolver sessions,
    ISettingsReferenceListReaderV1 colors, IErpAuthorityRevalidationService authority) : IManufacturingPaintSelectionSourceV1
{
    public async Task<ManufacturingPaintSelectionAnswerV1> ValidateAsync(Guid companyId, PlanPaintSelectionV1 selection,
        CancellationToken cancellationToken)
    {
        var actor = Capture(companyId);
        ManufacturingPaintSelectionAnswerV1 Answer(ManufacturingPaintSelectionOutcomeV1 outcome) =>
            new(outcome, companyId, outcome == ManufacturingPaintSelectionOutcomeV1.Available ? selection : null);
        if (!selection.IsWellFormed()) return Answer(ManufacturingPaintSelectionOutcomeV1.Ineligible);
        var product = await products.GetAsync(selection.ProductId, cancellationToken);
        RequireCurrent(companyId, actor);
        if (product.CompanyId != companyId || product.ProductId != selection.ProductId)
            return Answer(ManufacturingPaintSelectionOutcomeV1.Unavailable);
        if (product.Outcome is ProductPaintMembershipReadOutcomeV1.Unavailable or ProductPaintMembershipReadOutcomeV1.NoSource)
            return Answer(ManufacturingPaintSelectionOutcomeV1.Unavailable);
        if (product.Outcome is ProductPaintMembershipReadOutcomeV1.Inactive or ProductPaintMembershipReadOutcomeV1.NotFound)
            return Answer(ManufacturingPaintSelectionOutcomeV1.Ineligible);
        if (product.Outcome != ProductPaintMembershipReadOutcomeV1.Available || product.Entry is not { } entry
            || entry.ProductId != selection.ProductId || string.IsNullOrWhiteSpace(entry.SourceVersion)
            || string.IsNullOrWhiteSpace(entry.ProductVersion) || string.IsNullOrWhiteSpace(entry.MembershipVersion))
            return Answer(ManufacturingPaintSelectionOutcomeV1.Unavailable);
        if (entry.ProductVersion != selection.ProductVersion || entry.MembershipVersion != selection.MembershipVersion
            || entry.SourceVersion != selection.ProductSourceVersion)
            return Answer(ManufacturingPaintSelectionOutcomeV1.VersionConflict);

        var supplier = await suppliers.GetAuthorizedAsync(selection.SupplierPartyId, selection.SupplierCompanyAccountId, cancellationToken);
        RequireCurrent(companyId, actor);
        if (supplier.Outcome == PurchasingPartyAccountOutcomeV1.Denied) throw new UnauthorizedAccessException();
        if (supplier.Outcome == PurchasingPartyAccountOutcomeV1.NotFound) return Answer(ManufacturingPaintSelectionOutcomeV1.Ineligible);
        if (supplier.Outcome != PurchasingPartyAccountOutcomeV1.Found || supplier.Account is not { } account
            || account.CompanyId != companyId || account.PartyId != selection.SupplierPartyId
            || account.CompanyAccountId != selection.SupplierCompanyAccountId
            || string.IsNullOrWhiteSpace(account.PartyVersion) || string.IsNullOrWhiteSpace(account.AccountVersion))
            return Answer(ManufacturingPaintSelectionOutcomeV1.Unavailable);
        if (!account.PartyIsActive || !account.AccountIsActive || !account.AccountHasSupplierRole)
            return Answer(ManufacturingPaintSelectionOutcomeV1.Ineligible);
        if (account.PartyVersion != selection.SupplierPartyVersion || account.AccountVersion != selection.SupplierAccountVersion)
            return Answer(ManufacturingPaintSelectionOutcomeV1.VersionConflict);

        if (selection.ColorId is { } colorId)
        {
            var color = await colors.GetAsync(companyId, SettingsReferenceListKindV1.PaintColor, colorId, cancellationToken);
            RequireCurrent(companyId, actor);
            if (color.Outcome == SettingsReferenceListLookupOutcomeV1.Unavailable)
                return Answer(ManufacturingPaintSelectionOutcomeV1.Unavailable);
            if (color.Outcome is SettingsReferenceListLookupOutcomeV1.NotFound or SettingsReferenceListLookupOutcomeV1.Inactive)
                return Answer(ManufacturingPaintSelectionOutcomeV1.Ineligible);
            if (color.Outcome != SettingsReferenceListLookupOutcomeV1.Found || color.Entry is not { Active: true } value || value.Id != colorId)
                return Answer(ManufacturingPaintSelectionOutcomeV1.Unavailable);
            if (value.Version.ToString(CultureInfo.InvariantCulture) != selection.ColorVersion)
                return Answer(ManufacturingPaintSelectionOutcomeV1.VersionConflict);
        }
        return Answer(ManufacturingPaintSelectionOutcomeV1.Available);
    }

    public async Task<ManufacturingPaintCatalogV1> ListAsync(Guid companyId, string? supplierQuery, CancellationToken cancellationToken)
    {
        var actor = Capture(companyId);
        await RequireCatalogScopeAsync(companyId, actor, cancellationToken);
        if (supplierQuery is { Length: > 100 } || supplierQuery?.Any(char.IsControl) == true) throw new ArgumentException();
        var paints = await products.ListAsync(cancellationToken);
        await RequireCatalogScopeAsync(companyId, actor, cancellationToken);
        if (paints.CompanyId != companyId || paints.Outcome != ProductPaintMembershipCatalogOutcomeV1.Available
            || paints.Entries is null || paints.Entries.Any(x => x.ProductId == Guid.Empty
                || string.IsNullOrWhiteSpace(x.ProductVersion) || string.IsNullOrWhiteSpace(x.MembershipVersion)
                || string.IsNullOrWhiteSpace(x.SourceVersion))
            || paints.Entries.Select(x => x.ProductId).Distinct().Count() != paints.Entries.Count) throw new InvalidOperationException();

        var session = await sessions.ResolveAsync(cancellationToken);
        await RequireCatalogScopeAsync(companyId, actor, cancellationToken);
        if (string.IsNullOrWhiteSpace(session)) throw new InvalidOperationException();
        var page = await counterparties.GetAuthorizedPageAsync(new(new(supplierQuery?.Trim(), null, null, null, null,
            PartyRole.Supplier, CounterpartyRoleMatch.All, CounterpartyRecordState.Active, CounterpartyRecordState.Active,
            CounterpartySort.Code, CounterpartySortDirection.Ascending, 100, null, false), actor, session), cancellationToken);
        await RequireCatalogScopeAsync(companyId, actor, cancellationToken);
        if (page.Result == CounterpartyPageResult.Forbidden) throw new UnauthorizedAccessException();
        if (page.Result != CounterpartyPageResult.Succeeded || page.Page is null) throw new InvalidOperationException();
        var supplierList = page.Page.Items.Where(x => x.IsActive).SelectMany(p => p.CompanyAccounts
            .Where(a => a.CompanyId == companyId && a.IsActive && a.Roles.HasFlag(PartyRole.Supplier))
            .Select(a => new ManufacturingPaintSupplierV1(p.Id, a.Id, p.Version.ToString(CultureInfo.InvariantCulture),
                a.Version.ToString(CultureInfo.InvariantCulture), p.Code, p.TradeName ?? p.LegalName))).ToArray();

        // Color is optional. A genuinely disabled/empty list offers no catalog color; the user may leave it blank or use
        // the existing free color note. A failed owner read is never changed into an empty successful catalog.
        var colorList = await colors.ListAsync(companyId, SettingsReferenceListKindV1.PaintColor, cancellationToken);
        await RequireCatalogScopeAsync(companyId, actor, cancellationToken);
        if (colorList.CompanyId != companyId || colorList.Kind != SettingsReferenceListKindV1.PaintColor) throw new InvalidOperationException();
        return new(companyId, paints.Entries.Select(x => new ManufacturingPaintProductV1(x.ProductId, x.ProductVersion,
            x.MembershipVersion, x.SourceVersion, x.ProductCode, x.ProductName)).ToArray(), supplierList,
            colorList.Entries.Where(x => colorList.Enabled && x.Active).Select(x => new ManufacturingPaintColorV1(x.Id,
                x.Version.ToString(CultureInfo.InvariantCulture), x.Code, x.Name)).ToArray(), page.Page.HasMore);
    }

    private string Capture(Guid companyId)
    {
        RequireCurrent(companyId, user.UserId);
        return user.UserId!;
    }
    private async Task RequireCatalogScopeAsync(Guid companyId, string actor, CancellationToken cancellationToken)
    {
        RequireCurrent(companyId, actor);
        var grant = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ManufacturingPlansWrite, companyId, cancellationToken);
        RequireCurrent(companyId, actor);
        if (!grant.IsGranted || !grant.IsCompanyWide) throw new UnauthorizedAccessException();
    }
    private void RequireCurrent(Guid companyId, string? actor)
    {
        if (!company.IsResolved || companyId == Guid.Empty || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException();
    }
}
