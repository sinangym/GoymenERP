namespace GoymenERP.Modules.Work.Domain;

/// <summary>
/// W1-A measurement events of the Work inbox (DALGA_PLANI_v2 1.2: total wait and active review time measurable apart from
/// the first day). <see cref="Arrived"/> is the owner's own "waiting since" time of a pending cycle; <see cref="Opened"/>
/// is the item card opened in the inbox; <see cref="OwnerScreenOpened"/> is the link to the owner's decision screen
/// followed; <see cref="ResolvedObserved"/> is the first complete owner read that no longer lists the cycle (the owner's
/// decision, withdrawal or cancellation; recorded at observation time). No event stores who acted: there is no personal
/// performance report.
/// </summary>
public enum WorkApprovalInboxEventTypeV1
{
    Arrived = 1,
    Opened = 2,
    OwnerScreenOpened = 3,
    ResolvedObserved = 4,

    /// <summary>
    /// D10 (WP2): the owner's own decision of the cycle (approve, reject, withdraw or cancel), at the exact time the owner
    /// recorded it (<c>OccurredAtUtc</c>), read from the owner's decision record through its port — not the time Work noticed.
    /// </summary>
    Decided = 5,
}

/// <summary>One pending cycle as an owner listed it: the record and the time it began waiting.</summary>
public sealed record WorkApprovalInboxCycleV1(Guid ItemId, DateTimeOffset WaitingSinceUtc);

/// <summary>One interaction the inbox screen reports (<see cref="WorkApprovalInboxEventTypeV1.Opened"/> or
/// <see cref="WorkApprovalInboxEventTypeV1.OwnerScreenOpened"/>); <see cref="ClientEventId"/> makes a retry a replay.</summary>
public sealed record WorkApprovalInboxInteractionV1(
    Guid ClientEventId,
    string Kind,
    Guid ItemId,
    DateTimeOffset WaitingSinceUtc,
    WorkApprovalInboxEventTypeV1 EventType);

public enum WorkApprovalInboxInteractionOutcomeV1
{
    Recorded = 1,
    Replayed = 2,
    Conflict = 3,
}

/// <summary>
/// Work's own measurement store for the inbox. It never reads or writes another module's tables: cycles come from the
/// owners' read ports through the host. Rows are append-only and carry the company.
/// </summary>
public interface IWorkApprovalInboxMeasurementsV1
{
    /// <summary>
    /// Records <see cref="WorkApprovalInboxEventTypeV1.Arrived"/> for cycles not yet seen and, only when
    /// <paramref name="complete"/> (the owner listed every pending record), <see cref="WorkApprovalInboxEventTypeV1.ResolvedObserved"/>
    /// for recorded cycles the owner no longer lists. Concurrent readers may race: a duplicate is the same fact and is ignored.
    /// </summary>
    Task ObserveAsync(
        Guid companyId,
        string kind,
        IReadOnlyList<WorkApprovalInboxCycleV1> pending,
        bool complete,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// D10 (WP2): records the owner's decision of one cycle at the owner's own time. A cycle without an arrival is not
    /// recorded (nothing was measured); a second decision of the same cycle is the same fact and is ignored. Returns whether a
    /// row was written.
    /// </summary>
    Task<bool> RecordDecisionAsync(
        Guid companyId,
        string kind,
        WorkApprovalInboxCycleV1 cycle,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>WP2: the cycles of one kind that arrived and are neither observed resolved nor decided by the owner.</summary>
    Task<IReadOnlyList<WorkApprovalInboxCycleV1>> ListOpenCyclesAsync(
        Guid companyId,
        string kind,
        CancellationToken cancellationToken = default);

    Task<WorkApprovalInboxInteractionOutcomeV1> RecordInteractionAsync(
        Guid companyId,
        WorkApprovalInboxInteractionV1 interaction,
        CancellationToken cancellationToken = default);
}
