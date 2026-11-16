using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

public sealed class ManufacturingToleranceProfileLifecyclePolicyV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Profile = Guid.Parse("71000000-0000-4000-8000-000000000001");
    private static readonly Guid NextProfile = Guid.Parse("71000000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Action_names_are_separate_exact_declarations()
    {
        Assert.Equal("settings.manufacturing-tolerance-profiles.prepare",
            ManufacturingToleranceProfileActionsV1.Prepare);
        Assert.Equal("settings.manufacturing-tolerance-profiles.approve",
            ManufacturingToleranceProfileActionsV1.Approve);
        Assert.Equal("settings.manufacturing-tolerance-profiles.publish",
            ManufacturingToleranceProfileActionsV1.Publish);
        Assert.Equal(3, new[]
        {
            ManufacturingToleranceProfileActionsV1.Prepare,
            ManufacturingToleranceProfileActionsV1.Approve,
            ManufacturingToleranceProfileActionsV1.Publish,
        }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Prepare_binds_company_actor_reason_idempotency_boundary_and_explicit_version()
    {
        var result = Prepare();

        Assert.Equal((
                Company,
                Profile,
                SettingsManufacturingProcessesV1.Slitting,
                1,
                1,
                ManufacturingToleranceProfileStatusV1.Draft,
                "maker",
                At,
                "initial draft"),
            (
                result.CompanyId,
                result.ProfileId,
                result.Process,
                result.ProfileVersion,
                result.StateVersion,
                result.Status,
                result.PreparedBy,
                result.PreparedAtUtc,
                result.PreparationReason));
        Assert.Null(result.ApprovedBy);
        Assert.Null(result.PublishedBy);
    }

    [Theory]
    [InlineData("empty profile")]
    [InlineData("noncanonical process")]
    [InlineData("zero profile version")]
    [InlineData("nonzero new-state expectation")]
    [InlineData("empty company")]
    [InlineData("blank actor")]
    [InlineData("blank idempotency")]
    [InlineData("blank reason")]
    [InlineData("non-utc")]
    public void Prepare_rejects_malformed_command_boundaries(string fault)
    {
        var context = Context("maker", 0, "initial draft");
        var request = new PrepareManufacturingToleranceProfileV1(
            Profile, SettingsManufacturingProcessesV1.Slitting, 1, context);
        request = fault switch
        {
            "empty profile" => request with { ProfileId = Guid.Empty },
            "noncanonical process" => request with { Process = "slitting" },
            "zero profile version" => request with { ProfileVersion = 0 },
            "nonzero new-state expectation" => request with
            {
                Context = context with { ExpectedStateVersion = 1 },
            },
            "empty company" => request with { Context = context with { CompanyId = Guid.Empty } },
            "blank actor" => request with { Context = context with { ActorId = " " } },
            "blank idempotency" => request with { Context = context with { IdempotencyKey = "" } },
            "blank reason" => request with { Context = context with { Reason = "	" } },
            _ => request with
            {
                Context = context with
                {
                    OccurredAtUtc = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.FromHours(3)),
                },
            },
        };

        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.Invalid,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Prepare(request));
    }

    [Fact]
    public void Draft_revision_moves_maker_and_advances_only_the_state_version()
    {
        var draft = Prepare();
        var revised = ManufacturingToleranceProfileLifecyclePolicyV1.Revise(
            draft, Context("second-maker", draft.StateVersion, "corrected draft", 1));

        Assert.Equal(draft.ProfileVersion, revised.ProfileVersion);
        Assert.Equal(2, revised.StateVersion);
        Assert.Equal("second-maker", revised.PreparedBy);
        Assert.Equal(At.AddMinutes(1), revised.PreparedAtUtc);
        Assert.Equal("corrected draft", revised.PreparationReason);
        Assert.Equal(ManufacturingToleranceProfileStatusV1.Draft, revised.Status);
    }

    [Fact]
    public void Approval_is_retired_and_refuses_in_every_state_without_changing_the_profile()
    {
        var draft = Prepare();
        var published = Published();

        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.ApprovalRetired,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Approve(
                draft, Context("checker", draft.StateVersion, "checked", 1)));
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.ApprovalRetired,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Approve(
                draft, Context("maker", draft.StateVersion, "self approval", 1)));
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.ApprovalRetired,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Approve(
                published, Context("checker", published.StateVersion, "approve again", 2)));
        Assert.Equal(ManufacturingToleranceProfileStatusV1.Draft, draft.Status);
        Assert.Equal(1, draft.StateVersion);
    }

    [Fact]
    public void Preparer_may_publish_own_draft_directly_without_an_approval()
    {
        var draft = Prepare();
        var published = ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
            draft, Context("maker", draft.StateVersion, "published", 1));

        Assert.Equal(ManufacturingToleranceProfileStatusV1.Published, published.Status);
        Assert.Equal(2, published.StateVersion);
        Assert.Equal(("maker", At.AddMinutes(1), "published"),
            (published.PublishedBy, published.PublishedAtUtc, published.PublicationReason));
        Assert.Null(published.ApprovedBy);
        Assert.Null(published.ApprovedAtUtc);
        Assert.Null(published.ApprovalReason);
        Assert.Equal(("maker", At, "initial draft"),
            (published.PreparedBy, published.PreparedAtUtc, published.PreparationReason));
    }

    [Fact]
    public void Another_authorized_actor_may_publish_a_draft()
    {
        var draft = Prepare();
        var published = ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
            draft, Context("publisher", draft.StateVersion, "published", 1));

        Assert.Equal("publisher", published.PublishedBy);
        Assert.Null(published.ApprovedBy);
    }

    [Fact]
    public void Legacy_approved_version_publishes_keeping_its_approval_and_the_preparer_may_publish_it()
    {
        var approved = LegacyApproved();
        var published = ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
            approved, Context("maker", approved.StateVersion, "published", 2));

        Assert.Equal(ManufacturingToleranceProfileStatusV1.Published, published.Status);
        Assert.Equal(3, published.StateVersion);
        Assert.Equal(("checker", At.AddMinutes(1), "checked"),
            (published.ApprovedBy, published.ApprovedAtUtc, published.ApprovalReason));
        Assert.Equal(("maker", At.AddMinutes(2), "published"),
            (published.PublishedBy, published.PublishedAtUtc, published.PublicationReason));
    }

    [Fact]
    public void Legacy_history_shapes_stay_valid_and_a_self_approval_is_still_malformed()
    {
        var legacyPublished = LegacyApproved() with
        {
            StateVersion = 3,
            Status = ManufacturingToleranceProfileStatusV1.Published,
            PublishedBy = "checker",
            PublishedAtUtc = At.AddMinutes(2),
            PublicationReason = "published",
        };

        var next = ManufacturingToleranceProfileLifecyclePolicyV1.PrepareNextVersion(
            legacyPublished,
            new PrepareNextManufacturingToleranceProfileVersionV1(
                NextProfile, Context("maker-2", legacyPublished.StateVersion, "next version", 3)));
        Assert.Equal(2, next.ProfileVersion);

        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.Invalid,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
                LegacyApproved() with { ApprovedBy = "maker" }, Context("checker", 2, "published", 2)));
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.Invalid,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
                Prepare() with { ApprovalReason = "half approval" }, Context("checker", 1, "published", 1)));
    }

    [Fact]
    public void Expected_state_version_is_required_on_every_existing_transition()
    {
        var draft = Prepare();
        var failure = Assert.Throws<ManufacturingToleranceProfileLifecycleException>(() =>
            ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
                draft, Context("checker", 99, "published", 1)));

        Assert.Equal(ManufacturingToleranceProfileLifecycleCodesV1.VersionConflict, failure.Code);
        Assert.Equal(99, failure.ExpectedStateVersion);
        Assert.Equal(1, failure.CurrentStateVersion);
    }

    [Fact]
    public void Published_version_is_immutable_and_only_opens_a_distinct_next_version()
    {
        var published = Published();

        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.InvalidTransition,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Revise(
                published, Context("maker-2", published.StateVersion, "rewrite", 2)));
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.InvalidTransition,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
                published, Context("checker-2", published.StateVersion, "publish again", 2)));

        var next = ManufacturingToleranceProfileLifecyclePolicyV1.PrepareNextVersion(
            published,
            new PrepareNextManufacturingToleranceProfileVersionV1(
                NextProfile, Context("maker-2", published.StateVersion, "next version", 2)));

        Assert.Equal((
                published.CompanyId,
                NextProfile,
                published.Process,
                published.ProfileVersion + 1,
                1,
                ManufacturingToleranceProfileStatusV1.Draft,
                "maker-2"),
            (
                next.CompanyId,
                next.ProfileId,
                next.Process,
                next.ProfileVersion,
                next.StateVersion,
                next.Status,
                next.PreparedBy));
        Assert.Null(next.ApprovedBy);
        Assert.Null(next.PublishedBy);
    }

    [Fact]
    public void Next_version_requires_an_exact_published_predecessor_and_new_identity()
    {
        var draft = Prepare();
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.InvalidTransition,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.PrepareNextVersion(
                draft,
                new PrepareNextManufacturingToleranceProfileVersionV1(
                    NextProfile, Context("maker-2", draft.StateVersion, "next version", 1))));

        var published = Published();
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.Invalid,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.PrepareNextVersion(
                published,
                new PrepareNextManufacturingToleranceProfileVersionV1(
                    published.ProfileId, Context("maker-2", published.StateVersion, "next version", 2))));
    }

    [Fact]
    public void Company_scope_is_exact_and_malformed_state_is_not_repaired()
    {
        var draft = Prepare();
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.Invalid,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
                draft, Context("checker", draft.StateVersion, "published", 1) with { CompanyId = Guid.NewGuid() }));
        AssertCode(ManufacturingToleranceProfileLifecycleCodesV1.Invalid,
            () => ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
                draft with { PreparedBy = " " }, Context("checker", draft.StateVersion, "published", 1)));
    }

    [Fact]
    public void Contract_only_surface_contains_no_tolerance_value_or_runtime_activation()
    {
        Assert.Equal(
        [
            "CompanyId", "ProfileId", "Process", "ProfileVersion", "StateVersion", "Status",
            "PreparedBy", "PreparedAtUtc", "PreparationReason",
            "ApprovedBy", "ApprovedAtUtc", "ApprovalReason",
            "PublishedBy", "PublishedAtUtc", "PublicationReason",
        ], typeof(ManufacturingToleranceProfileLifecycleV1).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(typeof(ManufacturingToleranceProfileLifecycleV1).GetProperties(),
            property => property.Name.Contains("Percent", StringComparison.Ordinal)
                || property.Name.Contains("Tolerance", StringComparison.Ordinal)
                || property.Name.Contains("Grant", StringComparison.Ordinal));
    }

    private static ManufacturingToleranceProfileLifecycleV1 Prepare() =>
        ManufacturingToleranceProfileLifecyclePolicyV1.Prepare(
            new PrepareManufacturingToleranceProfileV1(
                Profile,
                SettingsManufacturingProcessesV1.Slitting,
                ProfileVersion: 1,
                Context("maker", 0, "initial draft")));

    private static ManufacturingToleranceProfileLifecycleV1 Published()
    {
        var draft = Prepare();
        return ManufacturingToleranceProfileLifecyclePolicyV1.Publish(
            draft, Context("maker", draft.StateVersion, "published", 1));
    }

    /// <summary>A version approved under D-MFG-PROFILE-AUTH-01=A before URN-D6 (state as stored).</summary>
    private static ManufacturingToleranceProfileLifecycleV1 LegacyApproved() =>
        Prepare() with
        {
            StateVersion = 2,
            Status = ManufacturingToleranceProfileStatusV1.Approved,
            ApprovedBy = "checker",
            ApprovedAtUtc = At.AddMinutes(1),
            ApprovalReason = "checked",
        };

    private static ManufacturingToleranceProfileCommandContextV1 Context(
        string actor,
        long expectedStateVersion,
        string reason,
        int minute = 0) =>
        new(
            Company,
            actor,
            expectedStateVersion,
            $"idempotency-{actor}-{expectedStateVersion}-{minute}",
            reason,
            At.AddMinutes(minute));

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<ManufacturingToleranceProfileLifecycleException>(action).Code);
}
