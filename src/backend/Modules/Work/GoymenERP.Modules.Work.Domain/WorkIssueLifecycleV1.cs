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
        var identity = reconciliation.State ?? prior;
        var issueKey = identity is null
            ? new WorkIssueKeyV1(observation.CompanyId, observation.SourceKey, observation.ScopeKey)
            : new WorkIssueKeyV1(identity.CompanyId, identity.SourceKey, identity.ScopeKey);

        return new WorkIssueEvaluationV1(issueKey, evaluationId, deliveryId, reconciliation);
    }
}
