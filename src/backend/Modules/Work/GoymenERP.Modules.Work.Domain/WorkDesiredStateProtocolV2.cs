namespace GoymenERP.Modules.Work.Domain;

public enum WorkDesiredStateDecisionV2
{
    Apply = 1,
    Replay = 2,
    IgnoreOlder = 3,
    Conflict = 4,
    Unavailable = 5,
    Refused = 6
}

public enum WorkSourceAvailabilityV2
{
    Available = 1,
    Unavailable = 2
}

public enum WorkDesiredStateV2
{
    NeedsAction = 1,
    Satisfied = 2
}

public sealed record WorkDesiredStateObservationV2(
    Guid CompanyId,
    string SourceKey,
    string ScopeKey,
    long? ObservationOrdinal,
    WorkSourceAvailabilityV2 Availability,
    WorkDesiredStateV2? DesiredState,
    Guid EvidenceId,
    string PayloadSha256);

public sealed record WorkEffectiveDesiredStateV2(
    long ObservationOrdinal,
    WorkDesiredStateV2 DesiredState,
    Guid EvidenceId,
    string PayloadSha256);

public sealed record WorkDesiredStateSnapshotV2(
    Guid CompanyId,
    string SourceKey,
    string ScopeKey,
    WorkDesiredStateObservationV2 HighestReliableObservation,
    WorkEffectiveDesiredStateV2? EffectiveState);

public sealed record WorkDesiredStateResultV2(
    WorkDesiredStateDecisionV2 Decision,
    WorkDesiredStateSnapshotV2? State);

public static class WorkDesiredStateProtocolV2
{
    public static WorkDesiredStateResultV2 Reconcile(
        WorkDesiredStateSnapshotV2? prior,
        WorkDesiredStateObservationV2 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ValidateObservation(observation, nameof(observation));

        if (prior is not null)
        {
            ValidateSnapshot(prior, nameof(prior));
            if (!SameScope(prior, observation))
            {
                return new(WorkDesiredStateDecisionV2.Refused, prior);
            }
        }

        if (observation.ObservationOrdinal is null)
        {
            return new(WorkDesiredStateDecisionV2.Unavailable, prior);
        }

        if (prior is null)
        {
            return new(
                WorkDesiredStateDecisionV2.Apply,
                CreateAppliedSnapshot(observation, effectiveState: null));
        }

        var admittedOrdinal = prior.HighestReliableObservation.ObservationOrdinal!.Value;
        var observedOrdinal = observation.ObservationOrdinal.Value;
        if (observedOrdinal < admittedOrdinal)
        {
            return new(WorkDesiredStateDecisionV2.IgnoreOlder, prior);
        }

        if (observedOrdinal == admittedOrdinal)
        {
            return new(
                observation == prior.HighestReliableObservation
                    ? WorkDesiredStateDecisionV2.Replay
                    : WorkDesiredStateDecisionV2.Conflict,
                prior);
        }

        return new(
            WorkDesiredStateDecisionV2.Apply,
            CreateAppliedSnapshot(observation, prior.EffectiveState));
    }

    private static WorkDesiredStateSnapshotV2 CreateAppliedSnapshot(
        WorkDesiredStateObservationV2 observation,
        WorkEffectiveDesiredStateV2? effectiveState)
    {
        if (observation.Availability == WorkSourceAvailabilityV2.Available)
        {
            effectiveState = new WorkEffectiveDesiredStateV2(
                observation.ObservationOrdinal!.Value,
                observation.DesiredState!.Value,
                observation.EvidenceId,
                observation.PayloadSha256);
        }

        return new WorkDesiredStateSnapshotV2(
            observation.CompanyId,
            observation.SourceKey,
            observation.ScopeKey,
            observation,
            effectiveState);
    }

    private static void ValidateSnapshot(WorkDesiredStateSnapshotV2 snapshot, string parameterName)
    {
        if (snapshot.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("The prior company identity is required.", parameterName);
        }

        ValidateText(snapshot.SourceKey, "prior source key", parameterName);
        ValidateText(snapshot.ScopeKey, "prior scope key", parameterName);
        if (snapshot.HighestReliableObservation is null)
        {
            throw new ArgumentException("The prior admitted observation is required.", parameterName);
        }

        ValidateObservation(snapshot.HighestReliableObservation, parameterName);
        if (snapshot.HighestReliableObservation.ObservationOrdinal is not long admittedOrdinal)
        {
            throw new ArgumentException("The prior admitted observation must have an ordinal.", parameterName);
        }

        if (!SameScope(snapshot, snapshot.HighestReliableObservation))
        {
            throw new ArgumentException("The prior admitted observation has a different scope.", parameterName);
        }

        var effective = snapshot.EffectiveState;
        if (effective is not null)
        {
            if (effective.ObservationOrdinal <= 0 || effective.ObservationOrdinal > admittedOrdinal)
            {
                throw new ArgumentException("The prior effective ordinal is outside the admitted range.", parameterName);
            }

            if (!Enum.IsDefined(effective.DesiredState))
            {
                throw new ArgumentException("The prior effective state is invalid.", parameterName);
            }

            if (effective.EvidenceId == Guid.Empty || !IsCanonicalSha256(effective.PayloadSha256))
            {
                throw new ArgumentException("The prior effective evidence is invalid.", parameterName);
            }
        }

        var admitted = snapshot.HighestReliableObservation;
        if (admitted.Availability == WorkSourceAvailabilityV2.Available)
        {
            if (effective is null
                || effective.ObservationOrdinal != admittedOrdinal
                || effective.DesiredState != admitted.DesiredState
                || effective.EvidenceId != admitted.EvidenceId
                || !string.Equals(effective.PayloadSha256, admitted.PayloadSha256, StringComparison.Ordinal))
            {
                throw new ArgumentException("The prior effective state does not match its admitted observation.", parameterName);
            }
        }
        else if (effective is not null && effective.ObservationOrdinal >= admittedOrdinal)
        {
            throw new ArgumentException("An unavailable observation cannot become effective.", parameterName);
        }
    }

    private static void ValidateObservation(WorkDesiredStateObservationV2 observation, string parameterName)
    {
        if (observation.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("The observation company identity is required.", parameterName);
        }

        ValidateText(observation.SourceKey, "source key", parameterName);
        ValidateText(observation.ScopeKey, "scope key", parameterName);
        if (observation.ObservationOrdinal is <= 0)
        {
            throw new ArgumentException("The observation ordinal must be positive when supplied.", parameterName);
        }

        if (!Enum.IsDefined(observation.Availability))
        {
            throw new ArgumentException("The source availability is invalid.", parameterName);
        }

        if (observation.DesiredState is not null && !Enum.IsDefined(observation.DesiredState.Value))
        {
            throw new ArgumentException("The desired state is invalid.", parameterName);
        }

        if (observation.EvidenceId == Guid.Empty)
        {
            throw new ArgumentException("The evidence identity is required.", parameterName);
        }

        if (!IsCanonicalSha256(observation.PayloadSha256))
        {
            throw new ArgumentException("The payload fingerprint must be exactly 64 lowercase ASCII hexadecimal characters.", parameterName);
        }

        if (observation.Availability == WorkSourceAvailabilityV2.Available)
        {
            if (observation.ObservationOrdinal is null || observation.DesiredState is null)
            {
                throw new ArgumentException("An available observation requires an ordinal and desired state.", parameterName);
            }
        }
        else if (observation.DesiredState is not null)
        {
            throw new ArgumentException("An unavailable observation cannot declare a desired state.", parameterName);
        }
    }

    private static void ValidateText(string value, string label, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException($"The {label} is invalid.", parameterName);
        }
    }

    private static bool IsCanonicalSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameScope(
        WorkDesiredStateSnapshotV2 snapshot,
        WorkDesiredStateObservationV2 observation) =>
        snapshot.CompanyId == observation.CompanyId
        && string.Equals(snapshot.SourceKey, observation.SourceKey, StringComparison.Ordinal)
        && string.Equals(snapshot.ScopeKey, observation.ScopeKey, StringComparison.Ordinal);
}
