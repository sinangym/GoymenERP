using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Shell;

/// <summary>
/// AT-018: the U-05 "Fiyatlar" screens (master v3 §27, "Liste, sürüm ve geçerlilik") — company-scoped price lists owned by
/// the Product module, so a provider of their own beside the global Product master's <see cref="ProductShellCapabilityProvider"/>
/// (whose <c>product.*</c> ids the registry reserves for the explicit global operations). The list and the detail need
/// <see cref="ProductPriceListPermissionsV1.Read"/>; the new-list screen <see cref="ProductPriceListPermissionsV1.Write"/>; the
/// detail's two controls are the draft edit / new version (<see cref="ProductPriceListPermissionsV1.Write"/>) and publication
/// (<see cref="ProductPriceListPermissionsV1.Publish"/>, its own name). Company-wide, single-company context. A declaration
/// grants nothing: the screens open only for an actor holding the names, and the grant is a human gate.
/// </summary>
public sealed class ProductPriceListShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "PRODUCT-PRICE-LISTS";
    public const string OwnerContractVersion = "1.0";

    public const string ListCapabilityId = "price-lists.list";
    public const string ListRoutePath = "/fiyat-listeleri";
    public const string CreateCapabilityId = "price-lists.create";
    public const string CreateRoutePath = "/fiyat-listeleri/yeni";
    public const string DetailCapabilityId = "price-lists.detail";
    public const string DetailRoutePath = "/fiyat-listeleri/:priceListId";
    public const string EditCapabilityId = "price-lists.edit";
    public const string PublishCapabilityId = "price-lists.publish";
    public const string ScreenId = "U-05";
    public const string CreateScreenId = "U-05-NEW";
    public const string DetailScreenId = "U-05-DETAIL";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [
        new ShellCapabilityDeclaration(ListCapabilityId, ListCapabilityId, ListRoutePath, ScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ProductPriceListPermissionsV1.Read], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(CreateCapabilityId, CreateCapabilityId, CreateRoutePath, CreateScreenId,
            ShellCapabilityRoutePrecedence.Static, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ProductPriceListPermissionsV1.Write], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(DetailCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ProductPriceListPermissionsV1.Read], ImplementationAvailable: true),
        new ShellCapabilityDeclaration(EditCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ProductPriceListPermissionsV1.Read, ProductPriceListPermissionsV1.Write],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
        new ShellCapabilityDeclaration(PublishCapabilityId, DetailCapabilityId, DetailRoutePath, DetailScreenId,
            ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide, [ProductPriceListPermissionsV1.Read, ProductPriceListPermissionsV1.Publish],
            ImplementationAvailable: true, ParentCapabilityId: DetailCapabilityId),
    ];
}
