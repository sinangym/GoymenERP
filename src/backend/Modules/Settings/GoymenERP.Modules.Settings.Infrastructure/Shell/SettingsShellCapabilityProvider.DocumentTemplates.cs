using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;

namespace GoymenERP.Modules.Settings.Infrastructure.Shell;

public sealed partial class SettingsShellCapabilityProvider
{
    /// <summary>STK-S29 Ayarlar routes use the Documents owner's registered permissions.
    /// A module's prepare and publish capabilities remain independent; the frontend uses a purpose gateway.
    /// Source declaration only: runtime acceptance must precede publishing these new screens.</summary>
    private static IReadOnlyCollection<ShellCapabilityDeclaration> DocumentTemplateDeclarations()
    {
        var declarations = new List<ShellCapabilityDeclaration>
        { TemplateRoute(ErpPermissions.DocumentsCompanyBrandingManage, "/ayarlar/belge-kimligi", "AY-BK-01") };
        foreach (var (prepare, publish, segment, screen) in new[]
                 { (ErpPermissions.DocumentsSalesTemplatePrepare, ErpPermissions.DocumentsSalesTemplatePublish, "satis", "AY-BS-SAT"),
                   (ErpPermissions.DocumentsInventoryTemplatePrepare, ErpPermissions.DocumentsInventoryTemplatePublish, "stok", "AY-BS-STK"),
                   (ErpPermissions.DocumentsLogisticsTemplatePrepare, ErpPermissions.DocumentsLogisticsTemplatePublish, "lojistik", "AY-BS-LOJ") })
        {
            var route = TemplateRoute(prepare, "/ayarlar/belge-sablonlari/" + segment, screen);
            // A staged, permission-neutral parent describes the shared workspace only.
            // Each purpose retains its own permission; publishing does not require preparing.
            var workspace = "settings.document-templates." + segment;
            declarations.Add(route with { CapabilityId = workspace, RequiredPermissions = [] });
            declarations.Add(route with { ParentCapabilityId = workspace });
            declarations.Add(route with { CapabilityId = publish, RequiredPermissions = [publish], ParentCapabilityId = workspace });
        }
        return declarations.AsReadOnly();
    }

    private static ShellCapabilityDeclaration TemplateRoute(string permission, string path, string screen) => new(
        permission, permission, path, screen, ShellCapabilityRoutePrecedence.Static,
        ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
        [permission], ImplementationAvailable: false);
}
