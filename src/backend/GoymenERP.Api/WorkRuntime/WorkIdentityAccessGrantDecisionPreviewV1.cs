using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Public approval-content binding token, not a private Audit proof or a claim of previously applied permissions.</summary>
public sealed record WorkIdentityAccessGrantSelectionV1(string ExpectedImpactDigest);

public sealed record WorkIdentityAccessGrantPreviewV1(IdentityAccessGrantRequestView Request,
    bool RequiresSecondApproval, bool TargetActive, bool RoleActive, IReadOnlyList<string> CurrentPermissions,
    bool CurrentRoleIsCritical, string ExpectedImpactDigest)
{
    internal static WorkIdentityAccessGrantPreviewV1 FromNative(IdentityAccessGrantDecisionDetailV1 detail) =>
        new(detail.Request, detail.RequiresSecondApproval, detail.TargetActive, detail.RoleActive,
            Array.AsReadOnly(detail.CurrentPermissions.ToArray()), detail.CurrentRoleIsCritical, detail.CurrentImpactDigest);
}
