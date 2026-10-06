using GoymenERP.Modules.Organization.Application.WorkingTime;

namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// The server-captured company and work-center selection for one complete working-time read. The
/// query window is half open: <see cref="FromUtc"/> is included and <see cref="ThroughUtc"/> is
/// excluded. This contract consumes UTC facts only; it does not resolve local recurrence or DST.
/// </summary>
public sealed record OrganizationWorkCenterWorkingTimeAvailabilityQueryV1
{
    public OrganizationWorkCenterWorkingTimeAvailabilityQueryV1(
        Guid companyId,
        Guid workCenterId,
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği zorunludur.", nameof(companyId));
        }

        if (workCenterId == Guid.Empty)
        {
            throw new ArgumentException("İş merkezi kimliği zorunludur.", nameof(workCenterId));
        }

        if (fromUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Başlangıç UTC olmalıdır.", nameof(fromUtc));
        }

        if (throughUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Bitiş UTC olmalıdır.", nameof(throughUtc));
        }

        if (fromUtc >= throughUtc)
        {
            throw new ArgumentException("Çalışma zamanı sorgu aralığı pozitif olmalıdır.", nameof(throughUtc));
        }

        CompanyId = companyId;
        WorkCenterId = workCenterId;
        FromUtc = fromUtc;
        ThroughUtc = throughUtc;
    }

    public Guid CompanyId { get; }

    public Guid WorkCenterId { get; }

    public DateTimeOffset FromUtc { get; }

    public DateTimeOffset ThroughUtc { get; }
}

public enum OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1
{
    Available = 1,
    NotFound = 2,
    Unavailable = 3,
}

/// <summary>
/// A complete answer from Organization. Empty intervals on <see cref="Available"/> mean known zero
/// availability over the whole query window. A missing, invalid or partially covered source must be
/// <see cref="Unavailable"/>, never an empty available snapshot.
/// </summary>
public sealed record OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1
{
    private OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1(
        OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1 outcome,
        WorkCenterWorkingTimeAvailabilitySnapshotV1? snapshot)
    {
        Outcome = outcome;
        Snapshot = snapshot;
    }

    public OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1 Outcome { get; }

    public WorkCenterWorkingTimeAvailabilitySnapshotV1? Snapshot { get; }

    public static OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1 Available(
        WorkCenterWorkingTimeAvailabilitySnapshotV1 snapshot) =>
        new(OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1.Available,
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    public static OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1 NotFound() =>
        new(OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1.NotFound, null);

    public static OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1 Unavailable() =>
        new(OrganizationWorkCenterWorkingTimeAvailabilityOutcomeV1.Unavailable, null);
}

/// <summary>
/// Organization-owned internal module read. The calling module supplies identities captured by its
/// server flow and authorizes its own operation. This snapshot grants no user authority and publishes
/// no provider, persistence, recurrence, queue, speed or throughput policy.
/// </summary>
public interface IOrganizationWorkCenterWorkingTimeAvailabilityV1
{
    Task<OrganizationWorkCenterWorkingTimeAvailabilityAnswerV1> ReadAsync(
        OrganizationWorkCenterWorkingTimeAvailabilityQueryV1 query,
        CancellationToken cancellationToken = default);
}
