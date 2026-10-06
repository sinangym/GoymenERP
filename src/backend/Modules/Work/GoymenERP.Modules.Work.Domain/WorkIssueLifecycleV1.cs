namespace GoymenERP.Modules.Work.Domain;

public readonly record struct WorkEvaluationIdV1
{
    public WorkEvaluationIdV1(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("An evaluation id is required.", nameof(value));

        Value = value;
    }

    public Guid Value { get; }
}

public readonly record struct WorkDeliveryIdV1
{
    public WorkDeliveryIdV1(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A delivery id is required.", nameof(value));

        Value = value;
    }

    public Guid Value { get; }
}

public sealed record WorkIssueKeyV1
{
    internal WorkIssueKeyV1(Guid companyId, string sourceKey, string scopeKey)
    {
        CompanyId = companyId;
        SourceKey = sourceKey;
        ScopeKey = scopeKey;
    }

    public Guid CompanyId { get; }
    public string SourceKey { get; }
    public string ScopeKey { get; }
}

public sealed record WorkIssueEvaluationV1
{
    internal WorkIssueEvaluationV1(
        WorkIssueKeyV1 issueKey,
        WorkEvaluationIdV1 evaluationId,
        WorkDeliveryIdV1 deliveryId,
        WorkDesiredStateResultV2 reconciliation)
    {
        IssueKey = issueKey;
        EvaluationId = evaluationId;
        DeliveryId = deliveryId;
        Reconciliation = reconciliation;
    }

    public WorkIssueKeyV1 IssueKey { get; }
    public WorkEvaluationIdV1 EvaluationId { get; }
    public WorkDeliveryIdV1 DeliveryId { get; }
    public WorkDesiredStateResultV2 Reconciliation { get; }
}

public static class WorkIssueLifecycleV1
{
    public static WorkIssueEvaluationV1 Evaluate(
        WorkDesiredStateSnapshotV2? prior,
        WorkDesiredStateObservationV2 observation,
        WorkEvaluationIdV1 evaluationId,
        WorkDeliveryIdV1 deliveryId)
    {
        if (evaluationId.Value == Guid.Empty)
            throw new ArgumentException("An evaluation id is required.", nameof(evaluationId));
        if (deliveryId.Value == Guid.Empty)
            throw new ArgumentException("A delivery id is required.", nameof(deliveryId));

        var reconciliation = WorkDesiredStateProtocolV2.Reconcile(prior, observation);
        return ProjectPersisted(reconciliation, observation, evaluationId, deliveryId);
    }

    public static WorkIssueEvaluationV1 ProjectPersisted(
        WorkDesiredStateResultV2 reconciliation,
        WorkDesiredStateObservationV2 observation,
        WorkEvaluationIdV1 evaluationId,
        WorkDeliveryIdV1 deliveryId)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        ValidateAdmissionInput(observation, evaluationId, deliveryId);
        if (reconciliation.Decision is WorkDesiredStateDecisionV2.Apply or WorkDesiredStateDecisionV2.Replay
            && reconciliation.State is null)
            throw new ArgumentException("An applied or replayed result requires its snapshot.", nameof(reconciliation));

        var identity = reconciliation.State;
        if (identity is not null
            && reconciliation.Decision != WorkDesiredStateDecisionV2.Refused
            && (identity.CompanyId != observation.CompanyId
                || !string.Equals(identity.SourceKey, observation.SourceKey, StringComparison.Ordinal)
                || !string.Equals(identity.ScopeKey, observation.ScopeKey, StringComparison.Ordinal)))
            throw new ArgumentException("The persisted result does not match its observation.", nameof(reconciliation));
        var issueKey = identity is null
            ? new WorkIssueKeyV1(observation.CompanyId, observation.SourceKey, observation.ScopeKey)
            : new WorkIssueKeyV1(identity.CompanyId, identity.SourceKey, identity.ScopeKey);

        return new WorkIssueEvaluationV1(issueKey, evaluationId, deliveryId, reconciliation);
    }

    public static void ValidateAdmissionInput(
        WorkDesiredStateObservationV2 observation,
        WorkEvaluationIdV1 evaluationId,
        WorkDeliveryIdV1 deliveryId)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (evaluationId.Value == Guid.Empty)
            throw new ArgumentException("An evaluation id is required.", nameof(evaluationId));
        if (deliveryId.Value == Guid.Empty)
            throw new ArgumentException("A delivery id is required.", nameof(deliveryId));
        if (observation.CompanyId == Guid.Empty
            || string.IsNullOrWhiteSpace(observation.SourceKey)
            || string.IsNullOrWhiteSpace(observation.ScopeKey)
            || !string.Equals(observation.SourceKey, observation.SourceKey.Trim(), StringComparison.Ordinal)
            || !string.Equals(observation.ScopeKey, observation.ScopeKey.Trim(), StringComparison.Ordinal)
            || observation.SourceKey.Any(char.IsControl)
            || observation.ScopeKey.Any(char.IsControl))
            throw new ArgumentException("An exact company, source and scope are required.", nameof(observation));
    }
}
