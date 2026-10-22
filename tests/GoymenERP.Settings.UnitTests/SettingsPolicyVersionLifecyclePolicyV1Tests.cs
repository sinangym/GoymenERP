using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class SettingsPolicyVersionLifecyclePolicyV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Policy = Guid.Parse("81000000-0000-4000-8000-000000000001");
    private static readonly Guid Version = Guid.Parse("82000000-0000-4000-8000-000000000001");
    private static readonly Guid NextVersion = Guid.Parse("82000000-0000-4000-8000-000000000002");
    private static readonly Guid Snapshot = Guid.Parse("83000000-0000-4000-8000-000000000001");
    private static readonly Guid NextSnapshot = Guid.Parse("83000000-0000-4000-8000-000000000002");
    private static readonly Guid ThirdSnapshot = Guid.Parse("83000000-0000-4000-8000-000000000003");
    private static readonly DateOnly BusinessDate = new(2026, 10, 22);
    private static readonly DateTimeOffset At = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Action_names_are_declarations_only_and_exact()
    {
        Assert.Equal("settings.policies.prepare", SettingsPolicyActionsV1.Prepare);
        Assert.Equal("settings.policies.approve", SettingsPolicyActionsV1.Approve);
        Assert.Equal("settings.policies.publish", SettingsPolicyActionsV1.Publish);
        Assert.Equal("settings.policies.publish-critical", SettingsPolicyActionsV1.PublishCritical);
        Assert.Equal(4, new[]
        {
            SettingsPolicyActionsV1.Prepare,
            SettingsPolicyActionsV1.Approve,
            SettingsPolicyActionsV1.Publish,
            SettingsPolicyActionsV1.PublishCritical,
        }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Separate_makers_complete_the_normal_lifecycle_with_explicit_snapshots()
    {
        var draft = Prepare(BusinessDate);
        var approved = Approve(draft, "checker", 1);
        var published = Publish(approved, "publisher", BusinessDate, 2);

        Assert.Equal(SettingsPolicyVersionStatusV1.Published, published.Status);
        Assert.Equal((1L, 3L), (published.PolicyVersion, published.StateVersion));
        Assert.Equal(("maker", "checker", "publisher"),
            (published.PreparedBy, published.ApprovedBy, published.PublishedBy));
        Assert.Equal(new[] { "maker" }, published.MakerHistory);
        Assert.Equal(("synthetic-family", "synthetic-schema-v1", "synthetic-scope"),
            (published.FamilyId, published.SchemaId, published.ScopeIdentity));
        Assert.Equal(Snapshot, published.ValueSnapshotId);
        Assert.Equal("synthetic-value", Assert.Single(published.Values).Value);
        Assert.Equal(BusinessDate, published.EffectiveFrom);
        Assert.Equal(BusinessDate, published.PublishedBusinessDate);
    }

    [Fact]
    public void Approver_may_publish_when_the_later_adapter_authorizes_both_actions()
    {
        var draft = Prepare(BusinessDate.AddDays(1));
        var approved = Approve(draft, "checker", 1);
        var published = Publish(approved, "checker", BusinessDate, 2);

        Assert.Equal("checker", published.ApprovedBy);
        Assert.Equal("checker", published.PublishedBy);
    }

    [Fact]
    public void Maker_cannot_approve_or_publish_own_policy_version()
    {
        var draft = Prepare(BusinessDate);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.MakerSeparationRequired,
            () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                draft, new ApproveSettingsPolicyVersionV1(Context("maker", draft, 1))));

        var approved = Approve(draft, "checker", 1);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.MakerSeparationRequired,
            () => SettingsPolicyVersionLifecyclePolicyV1.Publish(
                approved,
                new PublishSettingsPolicyVersionV1(Context("maker", approved, 2))));
    }

    [Fact]
    public void Every_draft_maker_remains_separated_after_multiple_revisions()
    {
        var draft = Prepare(BusinessDate);
        var revisedOnce = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            draft,
            new ReviseSettingsPolicyVersionV1(
                Revision(NextSnapshot, "revised-value", BusinessDate.AddDays(1)),
                Context("second-maker", draft, 1)));
        var revisedTwice = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            revisedOnce,
            new ReviseSettingsPolicyVersionV1(
                Revision(ThirdSnapshot, "revised-again", BusinessDate.AddDays(2)),
                Context("third-maker", revisedOnce, 2)));

        Assert.Equal((draft.PolicyId, draft.PolicyVersionId, 1L, 2L),
            (revisedOnce.PolicyId, revisedOnce.PolicyVersionId, revisedOnce.PolicyVersion, revisedOnce.StateVersion));
        Assert.Equal((draft.PolicyId, draft.PolicyVersionId, 1L, 3L),
            (revisedTwice.PolicyId, revisedTwice.PolicyVersionId, revisedTwice.PolicyVersion, revisedTwice.StateVersion));
        Assert.Equal("third-maker", revisedTwice.PreparedBy);
        Assert.Equal(ThirdSnapshot, revisedTwice.ValueSnapshotId);
        Assert.Equal(new[] { "maker", "second-maker", "third-maker" }, revisedTwice.MakerHistory);
        foreach (var maker in revisedTwice.MakerHistory)
        {
            AssertCode(SettingsPolicyVersionLifecycleCodesV1.MakerSeparationRequired,
                () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                    revisedTwice,
                    new ApproveSettingsPolicyVersionV1(Context(maker, revisedTwice, 3))));
        }

        var approved = Approve(revisedTwice, "checker", 4);
        var published = Publish(approved, "checker", BusinessDate, 5);
        Assert.Equal("checker", published.ApprovedBy);
        Assert.Equal("checker", published.PublishedBy);
        Assert.Equal(new[] { "maker", "second-maker", "third-maker" }, published.MakerHistory);
        var extendedHistory = published.MakerHistory.Add("not-a-maker");
        Assert.Contains("not-a-maker", extendedHistory);
        Assert.DoesNotContain("not-a-maker", published.MakerHistory);
    }

    [Fact]
    public void Company_identity_context_and_expected_state_version_must_match()
    {
        var draft = Prepare(BusinessDate);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                draft,
                new ApproveSettingsPolicyVersionV1(
                    Context("checker", draft, 1) with { CompanyId = Guid.NewGuid() })));
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                draft,
                new ApproveSettingsPolicyVersionV1(
                    Context("checker", draft, 1) with { PolicyVersionId = Guid.NewGuid() })));
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                draft,
                new ApproveSettingsPolicyVersionV1(
                    Context("checker", draft, 1) with { PolicyId = Guid.NewGuid() })));

        var failure = Assert.Throws<SettingsPolicyVersionLifecycleException>(() =>
            SettingsPolicyVersionLifecyclePolicyV1.Approve(
                draft,
                new ApproveSettingsPolicyVersionV1(Context("checker", draft, 99))));
        Assert.Equal(SettingsPolicyVersionLifecycleCodesV1.VersionConflict, failure.Code);
        Assert.Equal(99, failure.ExpectedStateVersion);
        Assert.Equal(1, failure.CurrentStateVersion);
    }

    [Fact]
    public void Malformed_command_context_is_rejected_through_the_policy_guard()
    {
        var draft = Prepare(BusinessDate);
        var valid = Context("checker", draft, 1);
        SettingsPolicyCommandContextV1[] malformed =
        [
            valid with { CompanyId = Guid.Empty },
            valid with { PolicyId = Guid.Empty },
            valid with { PolicyVersionId = Guid.Empty },
            valid with { ActorId = " checker " },
            valid with { IdempotencyKey = "" },
            valid with { IdempotencyKey = "bad\nkey" },
            valid with { IdempotencyKey = new string('k', 129) },
            valid with { Reason = " " },
            valid with { Reason = "bad\treason" },
            valid with { Reason = new string('r', 501) },
            valid with { ExpectedStateVersion = -1 },
            valid with { OccurredAtUtc = At.ToOffset(TimeSpan.FromHours(3)) },
            valid with { BusinessDateEuropeIstanbul = default },
        ];

        foreach (var context in malformed)
        {
            AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
                () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                    draft,
                    new ApproveSettingsPolicyVersionV1(context)));
        }
    }

    [Fact]
    public void Publication_is_immutable_and_successor_has_a_distinct_version_identity()
    {
        var published = Publish(Approve(Prepare(BusinessDate), "checker", 1), "checker", BusinessDate, 2);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.InvalidTransition,
            () => SettingsPolicyVersionLifecyclePolicyV1.Revise(
                published,
                new ReviseSettingsPolicyVersionV1(
                    Revision(NextSnapshot, "rewrite", BusinessDate),
                    Context("maker-2", published, 3))));
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.InvalidTransition,
            () => SettingsPolicyVersionLifecyclePolicyV1.Publish(
                published,
                new PublishSettingsPolicyVersionV1(Context("checker", published, 3))));

        var next = SettingsPolicyVersionLifecyclePolicyV1.PrepareNextVersion(
            published,
            new PrepareNextSettingsPolicyVersionV1(
                NextVersion,
                Revision(NextSnapshot, "successor-value", BusinessDate.AddDays(1)),
                Context("maker-2", published, 3)));

        Assert.Equal(published.PolicyId, next.PolicyId);
        Assert.NotEqual(published.PolicyVersionId, next.PolicyVersionId);
        Assert.Equal(NextVersion, next.PolicyVersionId);
        Assert.Equal(published.PolicyVersion + 1, next.PolicyVersion);
        Assert.Equal(1, next.StateVersion);
        Assert.Equal(SettingsPolicyVersionStatusV1.Draft, next.Status);
        Assert.Equal(published.ExactScope, next.ExactScope);
        Assert.Equal((published.FamilyId, published.SchemaId, published.ScopeIdentity),
            (next.FamilyId, next.SchemaId, next.ScopeIdentity));
        Assert.Equal("successor-value", Assert.Single(next.Values).Value);
        Assert.Equal(new[] { "maker-2" }, next.MakerHistory);

        var revisedNext = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            next,
            new ReviseSettingsPolicyVersionV1(
                Revision(ThirdSnapshot, "successor-revised", BusinessDate.AddDays(2)),
                Context("maker-3", next, 1)));
        Assert.Equal(new[] { "maker-2", "maker-3" }, revisedNext.MakerHistory);
        foreach (var maker in revisedNext.MakerHistory)
        {
            AssertCode(SettingsPolicyVersionLifecycleCodesV1.MakerSeparationRequired,
                () => SettingsPolicyVersionLifecyclePolicyV1.Approve(
                    revisedNext,
                    new ApproveSettingsPolicyVersionV1(Context(maker, revisedNext, 2))));
        }

        var approvedNext = Approve(revisedNext, "maker", 3);
        var publishedNext = Publish(approvedNext, "maker", BusinessDate, 4);
        Assert.Equal("maker", publishedNext.ApprovedBy);
        Assert.Equal("maker", publishedNext.PublishedBy);
    }

    [Fact]
    public void Publish_rejects_past_effective_date_but_accepts_explicit_current_and_future_dates()
    {
        var current = Approve(Prepare(BusinessDate), "checker", 1);
        Assert.Equal(SettingsPolicyVersionStatusV1.Published,
            Publish(current, "checker", BusinessDate, 2).Status);

        var future = Approve(Prepare(BusinessDate.AddDays(1)), "checker", 1);
        Assert.Equal(SettingsPolicyVersionStatusV1.Published,
            Publish(future, "checker", BusinessDate, 2).Status);

        var past = Approve(Prepare(BusinessDate.AddDays(-1)), "checker", 1);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.EffectiveDateInPast,
            () => Publish(past, "checker", BusinessDate, 2));
    }

    [Fact]
    public void Captured_scope_and_values_do_not_follow_external_collection_aliases()
    {
        var scope = new List<SettingsPolicySnapshotEntryV1>
        {
            new("synthetic-scope-key", "synthetic-scope-value"),
        };
        var values = new List<SettingsPolicySnapshotEntryV1>
        {
            new("synthetic-value-key", "synthetic-value"),
        };
        var draft = SettingsPolicyVersionLifecyclePolicyV1.Prepare(
            new PrepareSettingsPolicyVersionV1(
                SnapshotInput(BusinessDate, scope, values),
                Context("maker", expectedStateVersion: 0)));

        scope[0] = new("synthetic-scope-key", "mutated");
        values[0] = new("synthetic-value-key", "mutated");
        scope.Clear();
        values.Clear();

        Assert.Equal("synthetic-scope-value", Assert.Single(draft.ExactScope).Value);
        Assert.Equal("synthetic-value", Assert.Single(draft.Values).Value);
        var firstSnapshot = Assert.Single(draft.ValueSnapshotHistory);
        Assert.Equal(Snapshot, firstSnapshot.ValueSnapshotId);
        Assert.Equal("synthetic-value", Assert.Single(firstSnapshot.Values).Value);
        Assert.Empty(draft.ValueSnapshotHistory.Clear());
        Assert.Single(draft.ValueSnapshotHistory);
        Assert.Empty(firstSnapshot.Values.Clear());
        Assert.Equal("synthetic-value", Assert.Single(firstSnapshot.Values).Value);
        Assert.All(typeof(SettingsPolicyVersionLifecycleV1).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
        Assert.All(typeof(SettingsPolicyValueSnapshotV1).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    [Fact]
    public void Reusing_a_value_snapshot_identity_with_different_values_is_rejected()
    {
        var draft = Prepare(BusinessDate);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.Revise(
                draft,
                new ReviseSettingsPolicyVersionV1(
                    Revision(Snapshot, "different-value", BusinessDate),
                    Context("second-maker", draft, 1))));

        var published = Publish(Approve(draft, "checker", 1), "checker", BusinessDate, 2);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.PrepareNextVersion(
                published,
                new PrepareNextSettingsPolicyVersionV1(
                    NextVersion,
                    Revision(Snapshot, "different-successor-value", BusinessDate.AddDays(1)),
                    Context("second-maker", published, 3))));
    }

    [Fact]
    public void Historical_snapshot_identity_rejects_nonadjacent_content_change_but_allows_equal_content()
    {
        var draft = Prepare(BusinessDate);
        var second = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            draft,
            new ReviseSettingsPolicyVersionV1(
                Revision(NextSnapshot, "second-value", BusinessDate.AddDays(1)),
                Context("second-maker", draft, 1)));

        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.Revise(
                second,
                new ReviseSettingsPolicyVersionV1(
                    Revision(Snapshot, "changed-original-value", BusinessDate.AddDays(2)),
                    Context("third-maker", second, 2))));

        var reused = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            second,
            new ReviseSettingsPolicyVersionV1(
                Revision(Snapshot, "synthetic-value", BusinessDate.AddDays(2)),
                Context("third-maker", second, 2)));

        Assert.Equal(Snapshot, reused.ValueSnapshotId);
        Assert.Equal("synthetic-value", Assert.Single(reused.Values).Value);
        Assert.Equal(new[] { Snapshot, NextSnapshot, Snapshot },
            reused.ValueSnapshotHistory.Select(snapshot => snapshot.ValueSnapshotId).ToArray());
        Assert.All(
            reused.ValueSnapshotHistory.Where(snapshot => snapshot.ValueSnapshotId == Snapshot),
            snapshot => Assert.Equal("synthetic-value", Assert.Single(snapshot.Values).Value));
        Assert.Equal(3, reused.ValueSnapshotHistory.Length);
        Assert.Equal(2, reused.ValueSnapshotHistory.RemoveAt(0).Length);
        Assert.Equal(3, reused.ValueSnapshotHistory.Length);
    }

    [Fact]
    public void Successor_carries_snapshot_identity_history_while_starting_fresh_maker_history()
    {
        var firstDraft = Prepare(BusinessDate);
        var revisedFirst = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            firstDraft,
            new ReviseSettingsPolicyVersionV1(
                Revision(NextSnapshot, "second-value", BusinessDate.AddDays(1)),
                Context("second-maker", firstDraft, 1)));
        var publishedFirst = Publish(
            Approve(revisedFirst, "checker", 2),
            "checker",
            BusinessDate,
            3);
        var successor = SettingsPolicyVersionLifecyclePolicyV1.PrepareNextVersion(
            publishedFirst,
            new PrepareNextSettingsPolicyVersionV1(
                NextVersion,
                Revision(ThirdSnapshot, "successor-value", BusinessDate.AddDays(2)),
                Context("successor-maker", publishedFirst, 4)));

        Assert.Equal(new[] { Snapshot, NextSnapshot, ThirdSnapshot },
            successor.ValueSnapshotHistory.Select(snapshot => snapshot.ValueSnapshotId).ToArray());
        Assert.Equal(new[] { "successor-maker" }, successor.MakerHistory);
        AssertCode(SettingsPolicyVersionLifecycleCodesV1.Invalid,
            () => SettingsPolicyVersionLifecyclePolicyV1.Revise(
                successor,
                new ReviseSettingsPolicyVersionV1(
                    Revision(Snapshot, "changed-across-version", BusinessDate.AddDays(3)),
                    Context("successor-reviser", successor, 1))));

        var equalReuse = SettingsPolicyVersionLifecyclePolicyV1.Revise(
            successor,
            new ReviseSettingsPolicyVersionV1(
                Revision(Snapshot, "synthetic-value", BusinessDate.AddDays(3)),
                Context("successor-reviser", successor, 1)));
        Assert.Equal(new[] { Snapshot, NextSnapshot, ThirdSnapshot, Snapshot },
            equalReuse.ValueSnapshotHistory.Select(snapshot => snapshot.ValueSnapshotId).ToArray());
        Assert.Equal(new[] { "successor-maker", "successor-reviser" }, equalReuse.MakerHistory);
    }

    private static SettingsPolicyVersionLifecycleV1 Prepare(DateOnly effectiveFrom) =>
        SettingsPolicyVersionLifecyclePolicyV1.Prepare(
            new PrepareSettingsPolicyVersionV1(
                SnapshotInput(
                    effectiveFrom,
                    [new SettingsPolicySnapshotEntryV1("synthetic-scope-key", "synthetic-scope-value")],
                    [new SettingsPolicySnapshotEntryV1("synthetic-value-key", "synthetic-value")]),
                Context("maker", expectedStateVersion: 0)));

    private static SettingsPolicyVersionLifecycleV1 Approve(
        SettingsPolicyVersionLifecycleV1 draft,
        string actor,
        int minute) =>
        SettingsPolicyVersionLifecyclePolicyV1.Approve(
            draft,
            new ApproveSettingsPolicyVersionV1(Context(actor, draft, draft.StateVersion, minute)));

    private static SettingsPolicyVersionLifecycleV1 Publish(
        SettingsPolicyVersionLifecycleV1 approved,
        string actor,
        DateOnly businessDate,
        int minute) =>
        SettingsPolicyVersionLifecyclePolicyV1.Publish(
            approved,
            new PublishSettingsPolicyVersionV1(
                Context(actor, approved, approved.StateVersion, minute) with
                {
                    BusinessDateEuropeIstanbul = businessDate,
                }));

    private static SettingsPolicySnapshotInputV1 SnapshotInput(
        DateOnly effectiveFrom,
        IReadOnlyCollection<SettingsPolicySnapshotEntryV1> scope,
        IReadOnlyCollection<SettingsPolicySnapshotEntryV1> values) =>
        new(
            "synthetic-family",
            "synthetic-schema-v1",
            "synthetic-scope",
            scope,
            Snapshot,
            values,
            effectiveFrom);

    private static SettingsPolicyValueRevisionV1 Revision(
        Guid snapshotId,
        string value,
        DateOnly effectiveFrom) =>
        new(
            snapshotId,
            [new SettingsPolicySnapshotEntryV1("synthetic-value-key", value)],
            effectiveFrom);

    private static SettingsPolicyCommandContextV1 Context(
        string actor,
        long expectedStateVersion,
        int minute = 0) =>
        new(
            Company,
            Policy,
            Version,
            actor,
            expectedStateVersion,
            $"idempotency-{actor}-{expectedStateVersion}-{minute}",
            $"synthetic reason {actor}",
            At.AddMinutes(minute),
            BusinessDate);

    private static SettingsPolicyCommandContextV1 Context(
        string actor,
        SettingsPolicyVersionLifecycleV1 current,
        long expectedStateVersion,
        int minute = 0) =>
        new(
            current.CompanyId,
            current.PolicyId,
            current.PolicyVersionId,
            actor,
            expectedStateVersion,
            $"idempotency-{actor}-{expectedStateVersion}-{minute}",
            $"synthetic reason {actor}",
            At.AddMinutes(minute),
            BusinessDate);

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<SettingsPolicyVersionLifecycleException>(action).Code);
}
