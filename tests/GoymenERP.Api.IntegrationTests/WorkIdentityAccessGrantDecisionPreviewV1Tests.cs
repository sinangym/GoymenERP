using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.WorkInbox;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkIdentityAccessGrantDecisionPreviewV1Tests
{
    [Fact]
    public void Human_projection_preserves_actual_request_and_current_permissions_but_detaches_mutable_list()
    {
        var permissions = new List<string> { IdentityPermissionManifestV1.AccessApprove };
        var native = IdentityAccessGrantWorkDecisionHostTests.Facts(permissions);
        var projected = WorkIdentityAccessGrantPreviewV1.FromNative(native);
        Assert.Equal(native.Request, projected.Request);
        Assert.Equal(native.CurrentPermissions, projected.CurrentPermissions);
        Assert.Equal(native.CurrentImpactDigest, projected.ExpectedImpactDigest);
        Assert.Equal(native.RequiresSecondApproval, projected.RequiresSecondApproval);
        Assert.Equal(native.TargetActive, projected.TargetActive); Assert.Equal(native.RoleActive, projected.RoleActive);
        Assert.Equal(native.CurrentRoleIsCritical, projected.CurrentRoleIsCritical);
        permissions.Add(IdentityPermissionManifestV1.RolesManage);
        Assert.Single(projected.CurrentPermissions); Assert.NotSame(permissions, projected.CurrentPermissions);
    }

    [Theory] [InlineData("1")] [InlineData("9007199254740993")] [InlineData("9223372036854775807")]
    public void Native_text_versions_full_notes_and_dates_are_never_replaced_or_coerced(string version)
    {
        var native = IdentityAccessGrantWorkDecisionHostTests.Facts();
        native = native with { Request = native.Request with { Version = version, Reason = new('x', 500),
            RequestedAtUtc = DateTimeOffset.MinValue, ValidFromUtc = null, ValidUntilUtc = null } };
        var projected = WorkIdentityAccessGrantPreviewV1.FromNative(native);
        Assert.Equal(version, projected.Request.Version); Assert.Equal(500, projected.Request.Reason!.Length);
        Assert.Equal(DateTimeOffset.MinValue, projected.Request.RequestedAtUtc); Assert.Null(projected.Request.ValidUntilUtc);
        var wire = JsonSerializer.SerializeToElement(projected, Json);
        Assert.Equal(JsonValueKind.String, wire.GetProperty("request").GetProperty("version").ValueKind);
    }

    [Fact]
    public void Public_preview_exposes_only_the_explicit_seen_impact_token_and_keeps_all_private_provenance_out()
    {
        var native = IdentityAccessGrantWorkDecisionHostTests.Facts(); var human = WorkIdentityAccessGrantPreviewV1.FromNative(native);
        var item = IdentityAccessGrantDecisionBindingV1.Item(Company, native)!;
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, "1", true)
        { IdentityAccessGrantProof = IdentityAccessGrantDecisionBindingV1.Proof(Company, native) };
        var wire = JsonSerializer.SerializeToElement(human, Json);
        Assert.Equal(7, wire.EnumerateObject().Count()); Assert.Equal(12, wire.GetProperty("request").EnumerateObject().Count());
        Assert.Equal(native.CurrentImpactDigest, wire.GetProperty("expectedImpactDigest").GetString());
        var json = wire.GetRawText(); var snapshotJson = JsonSerializer.Serialize(snapshot, Json);
        foreach (var name in new[] { "sourceFactsDigest", "previewDigest", "currentImpactDigest", "identityAccessGrantProof", "originalAnswer", "appliedPermissionCount", "keyHash", "payloadHash", "receipt" })
        {
            Assert.DoesNotContain(name, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(name, snapshotJson, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(native.SourceFactsDigest, json); Assert.DoesNotContain(native.PreviewDigest, json);
    }

    [Theory] [InlineData(true, null)] [InlineData(false, "not_required")]
    public void Work_body_freezes_five_fields_with_one_exact_selection_and_has_no_human_permission_content(bool approve, string? reason)
    {
        var native = IdentityAccessGrantWorkDecisionHostTests.Facts();
        var request = new WorkDecisionRequestV1("1", approve, reason) { IdentityAccessGrant = new(native.CurrentImpactDigest) };
        var wire = JsonSerializer.SerializeToElement(request, Json);
        Assert.Equal(new[] { "approve", "delegationId", "expectedVersion", "identityAccessGrant", "reason" }, wire.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var selection = wire.GetProperty("identityAccessGrant"); Assert.Single(selection.EnumerateObject());
        Assert.Equal(native.CurrentImpactDigest, selection.GetProperty("expectedImpactDigest").GetString());
        Assert.DoesNotContain("currentPermissions", wire.GetRawText()); Assert.DoesNotContain("requester", wire.GetRawText());
        Assert.DoesNotContain("identityAccessGrant", JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), Json));
    }
}
