using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Product.Application.Contracts;
namespace GoymenERP.Modules.Product.Infrastructure.Shell;

/// <summary>Declaration only: real membership import/edit UI, no automatic assignment or Product runtime activation.</summary>
public sealed class ProductPaintMembershipShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "PRODUCT-PAINT-MEMBERSHIPS";
    public const string ManageCapabilityId = "paint-memberships.manage";
    public const string RoutePath = "/boya-urunleri";
    public const string ScreenId = "STK-S13";
    public string Owner => OwnerId;
    public string ContractVersion => "1.0";
    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
    [new(ManageCapabilityId, ManageCapabilityId, RoutePath, ScreenId, ShellCapabilityRoutePrecedence.Static,
        ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
        [ProductPaintMembershipPermissionsV1.Manage], ImplementationAvailable: true)];
}
