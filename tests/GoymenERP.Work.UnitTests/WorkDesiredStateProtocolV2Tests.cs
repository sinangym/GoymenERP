using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkDesiredStateProtocolV2Tests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EvidenceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EvidenceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void New_available_observation_is_applied_with_one_effective_state()
    {
        var observation = Available(1, WorkDesiredStateV2.NeedsAction);

        var result = WorkDesiredStateProtocolV2.Reconcile(null, observation);

        Assert.Equal(WorkDesiredStateDecisionV2.Apply, result.Decision);
        Assert.Equal(observation, result.State!.HighestReliableObservation);
        Assert.Equal(1, result.State.EffectiveState!.ObservationOrdinal);
        Assert.Equal(WorkDesiredStateV2.NeedsAction, result.State.EffectiveState.DesiredState);
    }

    [Fact]
    public void Same_observation_is_replayed_without_replacing_the_prior_snapshot()
    {
        var prior = Apply(null, Available(1, WorkDesiredStateV2.NeedsAction));

        var result = WorkDesiredStateProtocolV2.Reconcile(prior, prior.HighestReliableObservation);

        Assert.Equal(WorkDesiredStateDecisionV2.Replay, result.Decision);
        Assert.Same(prior, result.State);
    }

    [Fact]
    public void Same_ordinal_with_any_material_difference_conflicts_and_keeps_prior()
    {
        var prior = Apply(null, Available(1, WorkDesiredStateV2.NeedsAction));
        var conflicts = new[]
        {
            Unavailable(1),
            Available(1, WorkDesiredStateV2.Satisfied),
            Available(1, WorkDesiredStateV2.NeedsAction) with { EvidenceId = EvidenceB },
            Available(1, WorkDesiredStateV2.NeedsAction) with { PayloadSha256 = HashB }
        };

        foreach (var conflict in conflicts)
        {
            var result = WorkDesiredStateProtocolV2.Reconcile(prior, conflict);
            Assert.Equal(WorkDesiredStateDecisionV2.Conflict, result.Decision);
            Assert.Same(prior, result.State);
        }
    }

    [Fact]
    public void Older_observation_is_ignored_without_reopening_or_closing()
    {
        var prior = Apply(null, Available(4, WorkDesiredStateV2.Satisfied));

        var result = WorkDesiredStateProtocolV2.Reconcile(
            prior,
            Available(2, WorkDesiredStateV2.NeedsAction));

        Assert.Equal(WorkDesiredStateDecisionV2.IgnoreOlder, result.Decision);
        Assert.Same(prior, result.State);
        Assert.Equal(WorkDesiredStateV2.Satisfied, result.State!.EffectiveState!.DesiredState);
    }

    [Fact]
    public void Unavailable_watermark_blocks_late_satisfied_but_fresh_satisfied_can_close()
    {
        var v1Open = Apply(null, Available(1, WorkDesiredStateV2.NeedsAction));
        var v3Unavailable = WorkDesiredStateProtocolV2.Reconcile(v1Open, Unavailable(3));

        Assert.Equal(WorkDesiredStateDecisionV2.Apply, v3Unavailable.Decision);
        Assert.Equal(3, v3Unavailable.State!.HighestReliableObservation.ObservationOrdinal);
        Assert.Equal(1, v3Unavailable.State.EffectiveState!.ObservationOrdinal);
        Assert.Equal(WorkDesiredStateV2.NeedsAction, v3Unavailable.State.EffectiveState.DesiredState);

        var lateV2 = WorkDesiredStateProtocolV2.Reconcile(
            v3Unavailable.State,
            Available(2, WorkDesiredStateV2.Satisfied));
        Assert.Equal(WorkDesiredStateDecisionV2.IgnoreOlder, lateV2.Decision);
        Assert.Same(v3Unavailable.State, lateV2.State);

        var freshV4 = WorkDesiredStateProtocolV2.Reconcile(
            lateV2.State,
            Available(4, WorkDesiredStateV2.Satisfied));
        Assert.Equal(WorkDesiredStateDecisionV2.Apply, freshV4.Decision);
        Assert.Equal(4, freshV4.State!.HighestReliableObservation.ObservationOrdinal);
        Assert.Equal(4, freshV4.State.EffectiveState!.ObservationOrdinal);
        Assert.Equal(WorkDesiredStateV2.Satisfied, freshV4.State.EffectiveState.DesiredState);
    }

    [Fact]
    public void First_unavailable_observation_advances_only_the_reliable_watermark()
    {
        var result = WorkDesiredStateProtocolV2.Reconcile(null, Unavailable(3));

        Assert.Equal(WorkDesiredStateDecisionV2.Apply, result.Decision);
        Assert.Equal(3, result.State!.HighestReliableObservation.ObservationOrdinal);
        Assert.Null(result.State.EffectiveState);
    }

    [Fact]
    public void Versionless_unavailable_observation_does_not_invent_an_ordinal_or_state()
    {
        var prior = Apply(null, Available(1, WorkDesiredStateV2.NeedsAction));

        var result = WorkDesiredStateProtocolV2.Reconcile(prior, Unavailable(null));

        Assert.Equal(WorkDesiredStateDecisionV2.Unavailable, result.Decision);
        Assert.Same(prior, result.State);
        Assert.Equal(1, result.State!.HighestReliableObservation.ObservationOrdinal);
    }

    [Fact]
    public void Foreign_company_source_or_scope_is_refused_without_mutation()
    {
        var prior = Apply(null, Available(1, WorkDesiredStateV2.NeedsAction));
        var foreign = new[]
        {
            Available(2, WorkDesiredStateV2.Satisfied) with { CompanyId = CompanyB },
            Available(2, WorkDesiredStateV2.Satisfied) with { SourceKey = "source-b" },
            Available(2, WorkDesiredStateV2.Satisfied) with { ScopeKey = "work-b" }
        };

        foreach (var observation in foreign)
        {
            var result = WorkDesiredStateProtocolV2.Reconcile(prior, observation);
            Assert.Equal(WorkDesiredStateDecisionV2.Refused, result.Decision);
            Assert.Same(prior, result.State);
        }
    }

    [Fact]
    public void Malformed_public_observations_are_rejected_instead_of_normalized()
    {
        var malformed = new[]
        {
            Available(1, WorkDesiredStateV2.NeedsAction) with { CompanyId = Guid.Empty },
            Available(1, WorkDesiredStateV2.NeedsAction) with { SourceKey = " source-a" },
            Available(1, WorkDesiredStateV2.NeedsAction) with { ScopeKey = " " },
            Available(1, WorkDesiredStateV2.NeedsAction) with { ObservationOrdinal = 0 },
            Available(1, WorkDesiredStateV2.NeedsAction) with { Availability = (WorkSourceAvailabilityV2)99 },
            Available(1, WorkDesiredStateV2.NeedsAction) with { DesiredState = (WorkDesiredStateV2)99 },
            Available(1, WorkDesiredStateV2.NeedsAction) with { EvidenceId = Guid.Empty },
            Available(1, WorkDesiredStateV2.NeedsAction) with { PayloadSha256 = HashA.ToUpperInvariant() },
            Available(1, WorkDesiredStateV2.NeedsAction) with { PayloadSha256 = HashA[..63] },
            Available(1, WorkDesiredStateV2.NeedsAction) with { ObservationOrdinal = null },
            Available(1, WorkDesiredStateV2.NeedsAction) with { DesiredState = null },
            Unavailable(1) with { DesiredState = WorkDesiredStateV2.Satisfied }
        };

        foreach (var observation in malformed)
        {
            Assert.Throws<ArgumentException>(() =>
                WorkDesiredStateProtocolV2.Reconcile(null, observation));
        }
    }

    [Fact]
    public void Invalid_prior_invariants_are_rejected_before_any_decision()
    {
        var admitted = Available(3, WorkDesiredStateV2.NeedsAction);
        var invalidPriors = new[]
        {
            new WorkDesiredStateSnapshotV2(CompanyA, "source-a", "work-a", admitted, null),
            new WorkDesiredStateSnapshotV2(
                CompanyA,
                "source-a",
                "work-a",
                Unavailable(3),
                new WorkEffectiveDesiredStateV2(3, WorkDesiredStateV2.NeedsAction, EvidenceA, HashA)),
            new WorkDesiredStateSnapshotV2(
                CompanyA,
                "source-a",
                "work-a",
                Unavailable(3),
                new WorkEffectiveDesiredStateV2(4, WorkDesiredStateV2.NeedsAction, EvidenceA, HashA)),
            new WorkDesiredStateSnapshotV2(
                CompanyA,
                "source-a",
                "work-a",
                admitted with { ScopeKey = "work-b" },
                new WorkEffectiveDesiredStateV2(3, WorkDesiredStateV2.NeedsAction, EvidenceA, HashA))
        };

        foreach (var prior in invalidPriors)
        {
            Assert.Throws<ArgumentException>(() =>
                WorkDesiredStateProtocolV2.Reconcile(
                    prior,
                    Available(4, WorkDesiredStateV2.Satisfied)));
        }
    }

    [Fact]
    public void Null_observation_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WorkDesiredStateProtocolV2.Reconcile(null, null!));
    }

    private static WorkDesiredStateSnapshotV2 Apply(
        WorkDesiredStateSnapshotV2? prior,
        WorkDesiredStateObservationV2 observation)
    {
        var result = WorkDesiredStateProtocolV2.Reconcile(prior, observation);
        Assert.Equal(WorkDesiredStateDecisionV2.Apply, result.Decision);
        return result.State!;
    }

    private static WorkDesiredStateObservationV2 Available(
        long ordinal,
        WorkDesiredStateV2 desiredState) =>
        new(
            CompanyA,
            "source-a",
            "work-a",
            ordinal,
            WorkSourceAvailabilityV2.Available,
            desiredState,
            EvidenceA,
            HashA);

    private static WorkDesiredStateObservationV2 Unavailable(long? ordinal) =>
        new(
            CompanyA,
            "source-a",
            "work-a",
            ordinal,
            WorkSourceAvailabilityV2.Unavailable,
            null,
            EvidenceA,
            HashA);
}
