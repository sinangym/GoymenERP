using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Infrastructure.Shell;

public sealed partial class LogisticsShellCapabilityProvider
{
    public const string SubcontractListCapabilityId = "logistics.subcontract-deliveries.list";
    public const string SubcontractCreateCapabilityId = "logistics.subcontract-deliveries.create";
    public const string SubcontractDetailCapabilityId = "logistics.subcontract-deliveries.detail";
    public const string SubcontractRoutePath = "/lojistik/fason-teslimler";

    private static IReadOnlyCollection<ShellCapabilityDeclaration> SubcontractDeliveryDeclarations()
    {
        var detail = SubcontractDeclaration(SubcontractDetailCapabilityId, SubcontractRoutePath + "/:instructionId",
            "L-S3-DETAIL", ShellCapabilityRoutePrecedence.ParameterizedAfterStatic, [LogisticsPermissionManifestV1.InstructionsRead]);
        return
        [
            SubcontractDeclaration(SubcontractListCapabilityId, SubcontractRoutePath, "L-S3-LIST",
                ShellCapabilityRoutePrecedence.Static, [LogisticsPermissionManifestV1.InstructionsRead]),
            SubcontractDeclaration(SubcontractCreateCapabilityId, SubcontractRoutePath + "/yeni", "L-S3-CREATE",
                ShellCapabilityRoutePrecedence.Static, [LogisticsPermissionManifestV1.InstructionsCreate, ErpPermissions.InventoryPhysicalAllocationWrite]),
            detail,
            detail with { CapabilityId = "logistics.subcontract-deliveries.issue", ParentCapabilityId = SubcontractDetailCapabilityId,
                RequiredPermissions = [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.InstructionsCreate] },
            detail with { CapabilityId = "logistics.subcontract-deliveries.load", ParentCapabilityId = SubcontractDetailCapabilityId,
                RequiredPermissions = [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.LoadsRecord] },
            detail with { CapabilityId = "logistics.subcontract-deliveries.weigh", ParentCapabilityId = SubcontractDetailCapabilityId,
                RequiredPermissions = [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsRecord] },
            detail with { CapabilityId = "logistics.subcontract-deliveries.ship", ParentCapabilityId = SubcontractDetailCapabilityId,
                RequiredPermissions = [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.ShipmentsRecord, ErpPermissions.InventoryPhysicalAllocationWrite] },
            detail with { CapabilityId = "logistics.subcontract-deliveries.weighing-correct", ParentCapabilityId = SubcontractDetailCapabilityId,
                RequiredPermissions = [LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.WeighingsCorrect] },
        ];
    }

    // A declaration grants nothing; real SQL/browser/failure acceptance is still required.
    private static ShellCapabilityDeclaration SubcontractDeclaration(string id, string path, string screen,
        ShellCapabilityRoutePrecedence precedence, IReadOnlyCollection<string> permissions) => new(id, id, path, screen,
            precedence, ShellCapabilityContextRequirement.SingleCompany, ShellCapabilityScopeRequirement.CompanyWide,
            permissions, ImplementationAvailable: false);
}
