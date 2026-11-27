namespace GoymenERP.Modules.Work.Domain;

public sealed record WorkMorningSummaryCountV1(string Kind, int Count);

/// <summary>Deterministic counts of an existing released morning digest, not a model-generated report.
/// DigestId is the existing company/recipient/slot dedup identity. No record text, money or personnel name is copied.</summary>
public sealed record WorkMorningSummaryV1(Guid CompanyId, Guid DigestId, DateTimeOffset SlotUtc,
    string ScheduleVersion, IReadOnlyList<WorkMorningSummaryCountV1> Counts);

public interface IWorkMorningSummaryReadV1
{
    /// <summary>Server company/current actor only. Null means no released digest at the current scheduled morning slot.
    /// AI-D33: implements read and deterministic generation; record change and external send have no entry point here.
    /// Unreadable, unavailable or truncated source never produces partial counts. Does not create a scheduled task.</summary>
    Task<WorkMorningSummaryV1?> ReadAsync(CancellationToken cancellationToken = default);
}
