using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Identity.UnitTests;

public sealed partial class IdentityAccessAssignmentServiceTests
{
    [Fact]
    public async Task Purpose_read_contains_current_sorted_impact_without_requesting_general_identity_read()
    {
        var h = new Harness(); var seed = h.SeedQueue();
        var result = await h.Service().ReadDecisionAsync(seed.Older, h.Metadata(), _ => Task.CompletedTask);
        Assert.NotNull(result);
        Assert.Equal(seed.Older, result.Request.RequestId);
        Assert.Equal("1", result.Request.Version);
        Assert.True(result.RequiresSecondApproval);
        Assert.True(result.CurrentRoleIsCritical);
        Assert.Single(result.CurrentPermissions);
        Assert.All(h.Revalidation.Asked, x => Assert.Equal(IdentityPermissionManifestV1.AccessApprove, x.Permission));
        Assert.Empty(h.Audits.Enqueued);
        Assert.Empty(h.Database.CommandExecutions);
    }

    [Fact]
    public async Task Purpose_read_changed_role_permissions_changes_impact_without_pretending_request_version_changed()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var service = h.Service();
        var before = await service.ReadDecisionAsync(seed.Older, h.Metadata(), _ => Task.CompletedTask);
        Assert.NotNull(before);
        var role = await h.Database.Roles.SingleAsync(x => x.Id == before.Request.RoleId);
        role.AddPermission(IdentityPermissionManifestV1.AccessApprove);
        await h.Database.SaveChangesAsync(); h.Database.ChangeTracker.Clear();
        var after = await service.ReadDecisionAsync(seed.Older, h.Metadata(), _ => Task.CompletedTask);
        Assert.NotNull(after);
        Assert.Equal(before.Request.Version, after.Request.Version);
        Assert.Equal(before.SourceFactsDigest, after.SourceFactsDigest);
        Assert.NotEqual(before.CurrentImpactDigest, after.CurrentImpactDigest);
        Assert.NotEqual(before.PreviewDigest, after.PreviewDigest);
        Assert.Equal(2, after.CurrentPermissions.Count);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("membership")]
    public async Task Purpose_read_context_mismatch_releases_no_record(string fault)
    {
        var h = new Harness(); var seed = h.SeedQueue(); var context = h.Metadata();
        var service = fault == "membership" ? h.Service(currentActor: new TestUser(context.ActorId, true, [])) : h.Service();
        if (fault == "actor") context = context with { ActorId = Guid.NewGuid().ToString("D") };
        if (fault == "company") context = context with { CompanyId = CompanyB };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadDecisionAsync(seed.Older, context, _ => Task.CompletedTask));
        Assert.Empty(h.Audits.Enqueued);
    }

    [Fact]
    public async Task Final_consumer_normal_completion_with_revoked_authority_is_denied()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var calls = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Service().ReadDecisionAsync(seed.Older, h.Metadata(), _ =>
        { if (++calls == 6) h.Revalidation.Scope = Scope(false, false); return Task.CompletedTask; }));
        Assert.Equal(6, calls);
        Assert.Empty(h.Audits.Enqueued);
    }

    [Fact]
    public async Task Final_consumer_normal_completion_with_cancelled_token_releases_no_record()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var calls = 0; using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Service().ReadDecisionAsync(seed.Older, h.Metadata(), _ =>
        { if (++calls == 6) cts.Cancel(); return Task.CompletedTask; }, cts.Token));
        Assert.Equal(6, calls);
    }

    [Fact]
    public async Task Requester_and_target_are_excluded_only_when_native_second_approval_flag_requires_it()
    {
        var h = new Harness(); var seed = h.SeedQueue();
        var row = await h.Database.AccessGrantRequests.SingleAsync(x => x.Id == seed.Older);
        foreach (var actor in new[] { row.RequestedByUserId, row.TargetUserId })
        {
            var service = h.Service(currentActor: new TestUser(actor.ToString("D"), true, [CompanyA]));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadDecisionAsync(row.Id,
                h.Metadata() with { ActorId = actor.ToString("D") }, _ => Task.CompletedTask));
        }
    }

    [Fact]
    public async Task Purpose_approve_in_memory_remains_unavailable_and_does_not_create_an_assignment()
    {
        var h = new Harness(); var seed = h.SeedQueue(); var service = h.Service();
        var detail = await service.ReadDecisionAsync(seed.Older, h.Metadata(), _ => Task.CompletedTask);
        Assert.NotNull(detail);
        var result = await service.DecidePurposeAsync(seed.Older, true, "1", null, detail.CurrentImpactDigest, h.Metadata(), _ => Task.CompletedTask);
        Assert.Equal(503, result.Answer.StatusCode);
        Assert.Empty(h.Database.RoleAssignments);
        Assert.Empty(h.Audits.Enqueued);
        Assert.Empty(h.Database.CommandExecutions);
    }
}
