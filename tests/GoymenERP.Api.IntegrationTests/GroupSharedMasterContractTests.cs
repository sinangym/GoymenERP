using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.IntegrationTests;

// Pure contract tests with controlled server decisions: NOT live authorization or real SQL.
public sealed class GroupSharedMasterContractTests
{
    private static readonly Guid ActorA = Guid.Parse("53000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorB = Guid.Parse("53000000-0000-0000-0000-000000000002");
    private static readonly Guid OriginA = Guid.Parse("54000000-0000-0000-0000-000000000001");
    private static readonly Guid OriginB = Guid.Parse("54000000-0000-0000-0000-000000000002");
    private static readonly string KeyHash = new('A', 64);
    private static readonly string RequestHash = new('B', 64);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 99)]
    public async Task MissingReviewedAuthorityNeverGrantsGlobalAccess(int subject, int operation)
    {
        IGroupSharedMasterAuthorizationService service = new UnconfiguredGroupSharedMasterAuthorizationService();
        var result = await service.GetSnapshotAsync((GroupSharedMasterKind)subject,
            (GroupSharedMasterOperation)operation);
        Assert.False(result.IsGranted);
        Assert.Throws<UnauthorizedAccessException>(() => GroupSharedMasterCommandContext.FromServerDecision(
            result, GroupSharedMasterOperation.DraftCreate));
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("service")]
    [InlineData("read")]
    [InlineData("operation")]
    [InlineData("actor")]
    [InlineData("origin")]
    [InlineData("revision")]
    [InlineData("revision-trim")]
    [InlineData("revision-control")]
    public void IncompleteOrWrongScopeDecisionCannotBecomeAMutationContext(string mutation)
    {
        var decision = Decision();
        decision = mutation switch
        {
            "denied" => decision with { IsGranted = false },
            "service" => decision with { Subject = (GroupSharedMasterKind)2 },
            "read" => decision with { Operation = GroupSharedMasterOperation.Read },
            "operation" => decision with { Operation = GroupSharedMasterOperation.DraftBasicEdit },
            "actor" => decision with { LocalActorId = Guid.Empty },
            "origin" => decision with { OriginCompanyId = Guid.Empty },
            "revision" => decision with { AuthorityRevision = "" },
            "revision-trim" => decision with { AuthorityRevision = " controlled " },
            "revision-control" => decision with { AuthorityRevision = "controlled\n" },
            _ => throw new ArgumentException(nameof(mutation)),
        };
        Assert.Throws<UnauthorizedAccessException>(() => GroupSharedMasterCommandContext.FromServerDecision(
            decision, GroupSharedMasterOperation.DraftCreate));
    }

    [Fact]
    public void GlobalKeyDoesNotChangeWhenActorOrOriginChangesButReplayIsDenied()
    {
        var original = GroupSharedCommandBinding.Create(Context(), KeyHash, RequestHash);
        var otherActor = Context(Decision() with { LocalActorId = ActorB });
        var otherOrigin = Context(Decision() with { OriginCompanyId = OriginB });
        Assert.Equal(original.Key, GroupSharedCommandKey.Create(otherActor, KeyHash));
        Assert.Equal(original.Key, GroupSharedCommandKey.Create(otherOrigin, KeyHash));
        Assert.False(original.MatchesReauthorizedIntent(otherActor, KeyHash, RequestHash));
        Assert.False(original.MatchesReauthorizedIntent(otherOrigin, KeyHash, RequestHash));
        Assert.False(original.MatchesReauthorizedIntent(Context(), KeyHash, new string('C', 64)));
        Assert.False(original.MatchesReauthorizedIntent(Context(), new string('D', 64), RequestHash));
    }

    [Fact]
    public void SameIntentCanMatchAfterASeparateCurrentGrantedDecision()
    {
        var binding = GroupSharedCommandBinding.Create(Context(), KeyHash, RequestHash);
        var currentDecision = Decision() with { AuthorityRevision = "controlled-reviewed-revision-2" };
        Assert.True(binding.MatchesReauthorizedIntent(Context(currentDecision), KeyHash, RequestHash));
        var revoked = currentDecision with { IsGranted = false };
        Assert.Throws<UnauthorizedAccessException>(() => Context(revoked));
    }

    [Fact]
    public void CreateAndEditHaveDistinctVersionedGlobalCommandNamespaces()
    {
        var create = GroupSharedCommandKey.Create(Context(), KeyHash);
        var editContext = GroupSharedMasterCommandContext.FromServerDecision(
            Decision() with { Operation = GroupSharedMasterOperation.DraftBasicEdit },
            GroupSharedMasterOperation.DraftBasicEdit);
        var edit = GroupSharedCommandKey.Create(editContext, KeyHash);
        Assert.Equal("Product", create.Subject);
        Assert.Equal("product.draft.create.v1", create.CommandName);
        Assert.Equal("product.draft.basic-edit.v1", edit.CommandName);
        Assert.NotEqual(create, edit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void NonCanonicalHashesAreNotSilentlyNormalized(string hash)
    {
        Assert.Throws<ArgumentException>(() => GroupSharedCommandBinding.Create(Context(), hash, RequestHash));
        Assert.Throws<ArgumentException>(() => GroupSharedCommandBinding.Create(Context(), KeyHash, hash));
    }

    [Fact]
    public void GlobalAuditSubjectAndServerOriginAreNotAnOwnerCompanyId()
    {
        var draft = new GroupSharedAuditEventDraft(Context(), Guid.NewGuid(), "product.draft.created.v1",
            null, 1, "null", "{}", "Controlled contract test", "Test", null, null,
            "controlled-correlation", null, "controlled-key", AuditAiInvolvement.None, null);
        Assert.Equal("GroupSharedMaster", draft.SubjectKind);
        Assert.Equal("Product.Product", draft.EntityType);
        Assert.Equal(ActorA, draft.LocalActorId);
        Assert.Equal(OriginA, draft.OriginCompanyId);
        Assert.Null(typeof(GroupSharedAuditEventDraft).GetProperty("CompanyId"));
        Assert.Null(typeof(GroupSharedCommandKey).GetProperty("CompanyId"));
    }

    private static GroupSharedMasterAuthorizationSnapshot Decision() => new(true,
        GroupSharedMasterKind.Product, GroupSharedMasterOperation.DraftCreate,
        ActorA, OriginA, "controlled-reviewed-revision-1");

    private static GroupSharedMasterCommandContext Context(GroupSharedMasterAuthorizationSnapshot? decision = null) =>
        GroupSharedMasterCommandContext.FromServerDecision(decision ?? Decision(), GroupSharedMasterOperation.DraftCreate);
}
