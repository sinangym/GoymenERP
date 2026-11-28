using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed partial class IdentityAccessAssignmentServiceTests
{
    [Fact]
    public async Task Selected_work_read_uses_actual_readonly_owner_and_same_queue_formatter_without_writing()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var source = WorkSource(h);
        var context = WorkContext(h);
        var selected = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, seed.Older, default));
        var detail = await h.Service().ReadDecisionAsync(seed.Older, ReadContext(h), _ => Task.CompletedTask);
        Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesDecisionSnapshot(selected, detail));
        var queued = (await source.ListPendingAsync(context, 10, default)).Items!.Single(x => x.ItemId == seed.Older);
        Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesDecisionItem(queued, CompanyA, detail));
        Assert.True(selected.Pending); Assert.Equal("1", selected.SourceVersion);
        Assert.Equal(detail!.Request.RequestedBy.UserId.ToString("D"), selected.Item.PreparedBy);
        Assert.True(selected.Item.Excludes(detail.Request.Target.UserId.ToString("D")));
        Assert.DoesNotContain(h.Metadata().ActorId, selected.Item.ExcludedDeciders!);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(selected, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("identityAccessGrantProof", out _));
        Assert.DoesNotContain(detail.SourceFactsDigest, json.RootElement.GetRawText());
        Assert.DoesNotContain(detail.CurrentImpactDigest, json.RootElement.GetRawText());
        Assert.All(h.Revalidation.Asked, x => Assert.Equal(IdentityPermissionManifestV1.AccessApprove, x.Permission));
        Assert.Empty(h.Audits.Enqueued); Assert.Empty(h.Database.CommandExecutions); Assert.Empty(h.Database.RoleAssignments);
        Assert.False(h.Database.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Existing_pending_list_still_works_without_owner_but_selected_read_never_falls_back_to_it()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var source = WorkSource(h, missingOwner: true);
        Assert.Contains((await source.ListPendingAsync(WorkContext(h), 10, default)).Items!, x => x.ItemId == seed.Older);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(WorkContext(h), seed.Older, default));
    }

    [Theory]
    [InlineData("Pending")] [InlineData("Approved")] [InlineData("Rejected")] [InlineData("Withdrawn")]
    public async Task Selected_terminal_read_keeps_native_status_and_queue_membership_separate(string status)
    {
        var h = new Harness(); var seed = h.SeedQueue(); var row = await h.Database.AccessGrantRequests.SingleAsync(x => x.Id == seed.Older);
        if (status == "Approved") row.Approve(Guid.Parse(h.Metadata().ActorId), Now, Guid.NewGuid());
        if (status == "Rejected") row.Reject(Guid.Parse(h.Metadata().ActorId), "not_required", Now);
        if (status == "Withdrawn") row.Withdraw(row.RequestedByUserId, Now);
        await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await WorkSource(h).ReadDecisionAsync(WorkContext(h), seed.Older, default));
        Assert.Equal(status == "Pending", snapshot.Pending);
        Assert.Equal(status, snapshot.IdentityAccessGrantProof!.Status);
        Assert.Equal(status == "Pending" ? "1" : "2", snapshot.SourceVersion);
        Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesDecisionSnapshot(snapshot with { Pending = !snapshot.Pending },
            await h.Service().ReadDecisionAsync(seed.Older, ReadContext(h), _ => Task.CompletedTask)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_critical_requester_and_target_cannot_read_selected_human_facts(bool target)
    {
        var h = new Harness(); var seed = h.SeedQueue(); var row = await h.Database.AccessGrantRequests.SingleAsync(x => x.Id == seed.Older);
        var actor = (target ? row.TargetUserId : row.RequestedByUserId).ToString("D");
        var context = WorkContext(h) with { UserId = actor };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkSource(h, actor: actor).ReadDecisionAsync(context, seed.Older, default));
        Assert.Empty(h.Database.CommandExecutions); Assert.Empty(h.Audits.Enqueued);
    }

    [Theory]
    [InlineData("caption")] [InlineData("link")] [InlineData("reference")] [InlineData("rule")]
    [InlineData("maker")] [InlineData("excluded")] [InlineData("scope-kind")]
    public async Task Selected_binding_rejects_modified_native_item_before_any_command(string fault)
    {
        var h = new Harness(); var seed = h.SeedQueue();
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await WorkSource(h).ReadDecisionAsync(WorkContext(h), seed.Older, default));
        var item = snapshot.Item;
        item = fault switch
        {
            "caption" => item with { DocumentLabel = "other role" },
            "link" => item with { Link = "/ayarlar" },
            "reference" => item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }] },
            "rule" => item with { Rules = [item.Rules[0] with { Actual = "1" }] },
            "maker" => item with { PreparedBy = null },
            "excluded" => item with { ExcludedDeciders = null },
            _ => item with { Kind = "sales.request.v1" }
        };
        Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesDecisionProof(item, snapshot.IdentityAccessGrantProof));
        Assert.Empty(h.Database.CommandExecutions);
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("scoped")] [InlineData("permission")]
    public async Task Selected_context_denial_releases_no_human_body(string fault)
    {
        var h = new Harness(); var seed = h.SeedQueue(); var context = WorkContext(h);
        if (fault == "actor") context = context with { UserId = Guid.NewGuid().ToString("D") };
        if (fault == "company") context = context with { CompanyId = CompanyB };
        if (fault == "scoped") context = context with { GrantScope = new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) };
        if (fault == "permission") h.Revalidation.Scope = Scope(false, false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkSource(h).ReadDecisionAsync(context, seed.Older, default));
        Assert.Empty(h.Audits.Enqueued); Assert.Empty(h.Database.CommandExecutions);
    }

    [Fact]
    public async Task Current_role_permission_change_changes_current_proof_without_rewriting_immutable_request_facts()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var source = WorkSource(h); var context = WorkContext(h);
        var before = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(context, seed.Older, default));
        var role = await h.Database.Roles.SingleAsync(x => x.Id == before.IdentityAccessGrantProof!.RoleId);
        role.AddPermission(IdentityPermissionManifestV1.AccessApprove); await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        var current = await h.Service().ReadDecisionAsync(seed.Older, ReadContext(h), _ => Task.CompletedTask);
        Assert.Equal(before.IdentityAccessGrantProof!.SourceFactsDigest, current!.SourceFactsDigest);
        Assert.NotEqual(before.IdentityAccessGrantProof.CurrentImpactDigest, current.CurrentImpactDigest);
        Assert.True(IdentityAccessGrantDecisionBindingV1.MatchesDecisionItem(before.Item, CompanyA, current));
        Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesDecisionSnapshot(before, current));
    }

    [Fact]
    public async Task Precancelled_selected_read_performs_no_authority_or_owner_effect()
    {
        var h = new Harness(); var seed = h.SeedQueue(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkSource(h).ReadDecisionAsync(WorkContext(h), seed.Older, cts.Token));
        Assert.Empty(h.Revalidation.Asked); Assert.Empty(h.Database.CommandExecutions); Assert.Empty(h.Audits.Enqueued);
    }

    [Theory]
    [InlineData("01")] [InlineData("0")] [InlineData("-1")] [InlineData("9223372036854775808")]
    [InlineData("1\n")] [InlineData("1\r")]
    public void Work_versions_do_not_coerce_noncanonical_text(string version) => Assert.False(IdentityAccessGrantDecisionBindingV1.TryVersion(version, out _));

    [Fact]
    public async Task Stored_plain_request_does_not_invent_a_requester_or_target_exclusion()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var existing = await h.Database.AccessGrantRequests.SingleAsync(x => x.Id == seed.Older);
        var own = new GoymenERP.Modules.Identity.Domain.Approvals.AccessGrantRequest(Guid.NewGuid(), Guid.Parse(h.Metadata().ActorId),
            existing.TargetUserId, existing.RoleId, CompanyA, false, Now, null, null, null);
        h.Database.AccessGrantRequests.Add(own); await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        // Owner read joins the requester as well as the target; use an actual seeded requester identity for this fixture.
        var requester = new GoymenERP.Modules.Identity.Domain.Users.UserAccount(Guid.Parse(h.Metadata().ActorId), "SENTETIK requester", null);
        h.Database.Users.Add(requester); await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        var selected = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await WorkSource(h).ReadDecisionAsync(WorkContext(h), own.Id, default));
        Assert.Null(selected.Item.PreparedBy); Assert.Null(selected.Item.ExcludedDeciders);
        Assert.False(selected.Item.Excludes(h.Metadata().ActorId));
        Assert.False(selected.IdentityAccessGrantProof!.RequiresSecondApproval);
    }

    [Theory]
    [InlineData("permission")] [InlineData("cancel")]
    public async Task Normally_completed_authority_after_owner_query_cannot_release_late_denied_or_cancelled_facts(string fault)
    {
        var h = new Harness(); var seed = h.SeedQueue(); var calls = 0; using var cts = new CancellationTokenSource();
        h.Revalidation.BeforeFresh = () =>
        {
            if (++calls != 4) return;
            if (fault == "cancel") cts.Cancel(); else h.Revalidation.Scope = Scope(false, false);
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkSource(h).ReadDecisionAsync(WorkContext(h), seed.Older, cts.Token));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkSource(h).ReadDecisionAsync(WorkContext(h), seed.Older, default));
        Assert.Equal(4, calls); Assert.Empty(h.Database.CommandExecutions); Assert.Empty(h.Audits.Enqueued);
    }

    [Theory]
    [InlineData("source")] [InlineData("impact")] [InlineData("preview")]
    [InlineData("source-lf")] [InlineData("impact-cr")] [InlineData("preview-crlf")]
    [InlineData("request-body")] [InlineData("permissions")]
    public async Task Full_human_and_exact_digest_binding_rejects_modified_or_noncanonical_owner_shape(string fault)
    {
        var h = new Harness(); var seed = h.SeedQueue();
        var detail = Assert.IsType<IdentityAccessGrantDecisionDetailV1>(await h.Service().ReadDecisionAsync(seed.Older, ReadContext(h), _ => Task.CompletedTask));
        var original = IdentityAccessGrantDecisionBindingV1.Item(CompanyA, detail); Assert.NotNull(original);
        detail = fault switch
        {
            "source" => detail with { SourceFactsDigest = new('A', 64) },
            "impact" => detail with { CurrentImpactDigest = new('A', 64) },
            "preview" => detail with { PreviewDigest = new('A', 64) },
            "source-lf" => detail with { SourceFactsDigest = new string('A', 63) + "\n" },
            "impact-cr" => detail with { CurrentImpactDigest = new string('A', 63) + "\r" },
            "preview-crlf" => detail with { PreviewDigest = new string('A', 62) + "\r\n" },
            "request-body" => detail with { Request = detail.Request with { Reason = "changed request" } },
            _ => detail with { CurrentPermissions = [.. detail.CurrentPermissions, IdentityPermissionManifestV1.AccessApprove] }
        };
        Assert.Null(IdentityAccessGrantDecisionBindingV1.Item(CompanyA, detail));
        Assert.False(IdentityAccessGrantDecisionBindingV1.MatchesDecisionItem(original, CompanyA, detail));
    }

    private static WorkInboxSourceContextV1 WorkContext(Harness h) => new(CompanyA, h.Metadata().ActorId, h.Metadata().CorrelationId, h.Metadata().CausationId);
    private static IdentityAccessGrantDecisionReadContextV1 ReadContext(Harness h) => new(CompanyA, h.Metadata().ActorId, h.Metadata().CorrelationId, h.Metadata().CausationId);
    private static IdentityAccessGrantRequestWorkInboxSourceV1 WorkSource(Harness h, bool missingOwner = false, string? actor = null)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        var user = new TestUser(actor ?? h.Metadata().ActorId, true, [CompanyA]);
        return new(h.Database, company, user, h.Revalidation, missingOwner ? null : h.Service(context: company, currentActor: user));
    }
}
