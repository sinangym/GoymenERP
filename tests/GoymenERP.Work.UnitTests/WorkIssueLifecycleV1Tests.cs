using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkIssueLifecycleV1Tests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Evidence = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly WorkEvaluationIdV1 EvaluationA = new(
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
    private static readonly WorkEvaluationIdV1 EvaluationB = new(
        Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
    private static readonly WorkDeliveryIdV1 DeliveryA = new(
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
    private static readonly WorkDeliveryIdV1 DeliveryB = new(
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Issue_key_is_stable_across_observation_versions_while_evaluations_and_deliveries_are_distinct()
    {
        var first = WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction), EvaluationA, DeliveryA);
        var second = WorkIssueLifecycleV1.Evaluate(
            first.Reconciliation.State, Available(2, WorkDesiredStateV2.Satisfied),
            EvaluationB, DeliveryB);

        Assert.Equal(first.IssueKey, second.IssueKey);
        Assert.Equal(CompanyA, second.IssueKey.CompanyId);
        Assert.Equal("source-a", second.IssueKey.SourceKey);
        Assert.Equal("instruction-a", second.IssueKey.ScopeKey);
        Assert.NotEqual(first.EvaluationId, second.EvaluationId);
        Assert.NotEqual(first.DeliveryId, second.DeliveryId);
        Assert.Equal(WorkDesiredStateDecisionV2.Apply, second.Reconciliation.Decision);
    }

    [Fact]
    public void Different_instruction_scope_or_company_cannot_share_an_issue_key()
    {
        var first = WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction), EvaluationA, DeliveryA);
        var otherInstruction = WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction) with { ScopeKey = "instruction-b" },
            EvaluationB, DeliveryB);
        var otherCompany = WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction) with { CompanyId = CompanyB },
            EvaluationB, DeliveryB);

        Assert.NotEqual(first.IssueKey, otherInstruction.IssueKey);
        Assert.NotEqual(first.IssueKey, otherCompany.IssueKey);
    }

    [Fact]
    public void Same_delivery_and_observation_replays_without_a_second_state_change()
    {
        var observation = Available(1, WorkDesiredStateV2.NeedsAction);
        var first = WorkIssueLifecycleV1.Evaluate(null, observation, EvaluationA, DeliveryA);
        var replay = WorkIssueLifecycleV1.Evaluate(
            first.Reconciliation.State, observation, EvaluationB, DeliveryA);

        Assert.Equal(WorkDesiredStateDecisionV2.Apply, first.Reconciliation.Decision);
        Assert.Equal(WorkDesiredStateDecisionV2.Replay, replay.Reconciliation.Decision);
        Assert.Same(first.Reconciliation.State, replay.Reconciliation.State);
        Assert.Equal(first.IssueKey, replay.IssueKey);
        Assert.Equal(DeliveryA, replay.DeliveryId);
    }

    [Fact]
    public void Stale_observation_cannot_reopen_a_satisfied_issue()
    {
        var satisfied = WorkIssueLifecycleV1.Evaluate(
            null, Available(4, WorkDesiredStateV2.Satisfied), EvaluationA, DeliveryA);
        var stale = WorkIssueLifecycleV1.Evaluate(
            satisfied.Reconciliation.State, Available(2, WorkDesiredStateV2.NeedsAction),
            EvaluationB, DeliveryB);

        Assert.Equal(WorkDesiredStateDecisionV2.IgnoreOlder, stale.Reconciliation.Decision);
        Assert.Same(satisfied.Reconciliation.State, stale.Reconciliation.State);
        Assert.Equal(WorkDesiredStateV2.Satisfied,
            stale.Reconciliation.State!.EffectiveState!.DesiredState);
    }

    [Fact]
    public void Unavailable_watermark_cannot_close_an_issue_or_be_overridden_by_late_satisfaction()
    {
        var open = WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction), EvaluationA, DeliveryA);
        var unavailable = WorkIssueLifecycleV1.Evaluate(
            open.Reconciliation.State, Unavailable(3), EvaluationB, DeliveryB);
        var late = WorkIssueLifecycleV1.Evaluate(
            unavailable.Reconciliation.State, Available(2, WorkDesiredStateV2.Satisfied),
            EvaluationA, DeliveryA);

        Assert.Equal(3, unavailable.Reconciliation.State!.HighestReliableObservation.ObservationOrdinal);
        Assert.Equal(WorkDesiredStateV2.NeedsAction,
            unavailable.Reconciliation.State.EffectiveState!.DesiredState);
        Assert.Equal(WorkDesiredStateDecisionV2.IgnoreOlder, late.Reconciliation.Decision);
        Assert.Same(unavailable.Reconciliation.State, late.Reconciliation.State);
    }

    [Fact]
    public void Versionless_unavailable_and_foreign_scope_do_not_change_prior_identity_or_state()
    {
        var open = WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction), EvaluationA, DeliveryA);
        var unavailable = WorkIssueLifecycleV1.Evaluate(
            open.Reconciliation.State, Unavailable(null), EvaluationB, DeliveryB);
        var foreign = WorkIssueLifecycleV1.Evaluate(
            open.Reconciliation.State,
            Available(2, WorkDesiredStateV2.Satisfied) with { ScopeKey = "instruction-b" },
            EvaluationB, DeliveryB);

        Assert.Equal(WorkDesiredStateDecisionV2.Unavailable, unavailable.Reconciliation.Decision);
        Assert.Equal(WorkDesiredStateDecisionV2.Refused, foreign.Reconciliation.Decision);
        Assert.Same(open.Reconciliation.State, unavailable.Reconciliation.State);
        Assert.Same(open.Reconciliation.State, foreign.Reconciliation.State);
        Assert.Equal(open.IssueKey, foreign.IssueKey);
    }

    [Fact]
    public void Empty_ids_and_malformed_observation_are_refused_before_projection()
    {
        Assert.Throws<ArgumentException>(() => new WorkEvaluationIdV1(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new WorkDeliveryIdV1(Guid.Empty));
        Assert.Throws<ArgumentException>(() => WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction), default, DeliveryA));
        Assert.Throws<ArgumentException>(() => WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction), EvaluationA, default));
        Assert.Throws<ArgumentException>(() => WorkIssueLifecycleV1.Evaluate(
            null, Available(1, WorkDesiredStateV2.NeedsAction) with { SourceKey = " source-a" },
            EvaluationA, DeliveryA));
    }

    private static WorkDesiredStateObservationV2 Available(
        long ordinal, WorkDesiredStateV2 state) => new(
        CompanyA, "source-a", "instruction-a", ordinal,
        WorkSourceAvailabilityV2.Available, state, Evidence, Hash);

    private static WorkDesiredStateObservationV2 Unavailable(long? ordinal) => new(
        CompanyA, "source-a", "instruction-a", ordinal,
        WorkSourceAvailabilityV2.Unavailable, null, Evidence, Hash);
}
