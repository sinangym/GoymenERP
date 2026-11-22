using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Modules.Work.Infrastructure.Shell;

/// <summary>
/// WP9: Work's own shell capabilities, declared with Work's own permissions (registered, granted to no role — a human gate).
/// <list type="bullet">
/// <item><c>work.tasks.mine</c> — "Bana atanan" (<c>/gorevlerim</c>) for a holder of <c>work.tasks.complete</c>: the page lists
/// only the caller's own tasks and its one command is completing them.</item>
/// <item><c>work.tasks.assign</c> — the assign control on that page; a control keeps its parent's permissions and adds
/// <c>work.tasks.assign</c>.</item>
/// </list>
/// Not declared, on purpose: the personal notification inbox (<c>/bildirimlerim</c>) is recipient-only and needs no
/// permission, and the record notes panel lives on each owner's detail screen behind that owner's own read — the registry
/// refuses a permissionless "available" capability, so neither is gated here; the server decides each read and command.
/// <item><c>work.approvals.follow-up</c> — WP2 (YOL-D17, YOL-D28): "Yükseltilen ve sahipsiz onaylar" (<c>/onay-takibi</c>) for a
/// holder of <c>work.approvals.escalate</c>; the page lists Work's own follow-up rows and decides nothing.</item>
/// <para>1.0 (WP9): first version.</para>
/// <para>1.1 (WP2): adds <c>work.approvals.follow-up</c>.</para>
/// </summary>
public sealed class WorkShellCapabilityProvider : IShellCapabilityProvider
{
    public const string OwnerId = "WORK";
    public const string OwnerContractVersion = "1.1";
    public const string MyTasksCapabilityId = "work.tasks.mine";
    public const string MyTasksRouteId = "work.tasks-mine";
    public const string MyTasksRoutePath = "/gorevlerim";
    public const string MyTasksScreenId = "W-TASKS-01";
    public const string AssignTaskCapabilityId = "work.tasks.assign";
    public const string ApprovalFollowUpCapabilityId = "work.approvals.follow-up";
    public const string ApprovalFollowUpRouteId = "work.approvals-follow-up";
    public const string ApprovalFollowUpRoutePath = "/onay-takibi";
    public const string ApprovalFollowUpScreenId = "W-APPROVAL-FOLLOW-UP-01";

    public string Owner => OwnerId;

    public string ContractVersion => OwnerContractVersion;

    public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations()
    {
        var myTasks = new ShellCapabilityDeclaration(
            MyTasksCapabilityId,
            MyTasksRouteId,
            MyTasksRoutePath,
            MyTasksScreenId,
            ShellCapabilityRoutePrecedence.Static,
            ShellCapabilityContextRequirement.SingleCompany,
            ShellCapabilityScopeRequirement.CompanyWide,
            [WorkPermissionManifestV1.TasksComplete],
            ImplementationAvailable: true);
        return
        [
            myTasks,
            myTasks with
            {
                CapabilityId = AssignTaskCapabilityId,
                ParentCapabilityId = MyTasksCapabilityId,
                RequiredPermissions = [WorkPermissionManifestV1.TasksComplete, WorkPermissionManifestV1.TasksAssign],
            },
            new ShellCapabilityDeclaration(
                ApprovalFollowUpCapabilityId,
                ApprovalFollowUpRouteId,
                ApprovalFollowUpRoutePath,
                ApprovalFollowUpScreenId,
                ShellCapabilityRoutePrecedence.Static,
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [WorkPermissionManifestV1.ApprovalsEscalate],
                ImplementationAvailable: true),
        ];
    }
}
