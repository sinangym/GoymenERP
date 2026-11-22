using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Collaboration;

/// <summary>
/// One scheduler cycle for the server-resolved company (AI-D29): (1) an open task past its due time gives its assignee one
/// "şimdi" overdue notification (the unique recipient/kind/source key keeps it to one); (2) at the latest digest slot every
/// held "özet" notification recorded up to that slot is released into the recipient's digest for that slot. Without an
/// available schedule nothing is released (the digest class stays held, never shown early). Derived rows only: no business
/// record changes, nothing leaves Work.
/// </summary>
public sealed class WorkCollaborationMaintenanceV1(WorkDbContext database, ICompanyContext company,
    IWorkDigestScheduleReaderV1 schedules, TimeProvider time) : IWorkCollaborationMaintenanceV1
{
    public async Task<WorkCollaborationCycleV1> RunCycleAsync(CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId
            : throw new InvalidOperationException("A resolved company is required for the collaboration cycle.");
        var now = time.GetUtcNow();
        var correlationId = "work-collaboration-cycle-" + Guid.NewGuid().ToString("N");
        var schedule = await schedules.ReadAsync(cancellationToken);
        RequireCompany(companyId);
        if (schedule is null || schedule.CompanyId != companyId) schedule = WorkDigestScheduleV1.Unavailable(companyId);
        var slot = WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule, now);
        try
        {
            await using var transaction = database.Database.IsRelational()
                ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            var overdue = await database.Tasks.AsNoTracking()
                .Where(task => task.CompanyId == companyId && task.Status == (int)WorkTaskStatusV1.Open && task.DueAtUtc <= now
                    && !database.InboxNotifications.Any(item => item.CompanyId == companyId && item.RecipientUserId == task.AssigneeUserId
                        && item.Kind == (int)WorkInboxNotificationKindV1.TaskOverdue && item.SourceId == task.Id))
                .OrderBy(task => task.Id)
                .Select(task => new { task.Id, task.AssigneeUserId, task.RecordType, task.RecordId })
                .ToArrayAsync(cancellationToken);
            foreach (var task in overdue)
                database.InboxNotifications.Add(new WorkInboxNotificationRow
                {
                    CompanyId = companyId, Id = Guid.NewGuid(), RecipientUserId = task.AssigneeUserId,
                    Kind = (int)WorkInboxNotificationKindV1.TaskOverdue,
                    Class = (int)WorkInboxNotificationClassesV1.Of(WorkInboxNotificationKindV1.TaskOverdue), SourceId = task.Id,
                    RecordType = task.RecordType, RecordId = task.RecordId, ActorUserId = null, RecordedAtUtc = now, CorrelationId = correlationId,
                });

            var digests = 0;
            var items = 0;
            if (slot is { } releasedSlot)
            {
                var held = await database.InboxNotifications.AsNoTracking()
                    .Where(item => item.CompanyId == companyId && item.Class == (int)WorkInboxNotificationClassV1.Digest
                        && item.RecordedAtUtc <= releasedSlot
                        && !database.InboxDigestItems.Any(link => link.CompanyId == companyId && link.NotificationId == item.Id))
                    .OrderBy(item => item.RecipientUserId).ThenBy(item => item.Id)
                    .Select(item => new { item.Id, item.RecipientUserId })
                    .ToArrayAsync(cancellationToken);
                foreach (var recipient in held.GroupBy(item => item.RecipientUserId))
                {
                    var existing = await database.InboxDigests.AsNoTracking()
                        .Where(digest => digest.CompanyId == companyId && digest.RecipientUserId == recipient.Key && digest.SlotUtc == releasedSlot)
                        .Select(digest => (Guid?)digest.Id).SingleOrDefaultAsync(cancellationToken);
                    var digestId = existing ?? Guid.NewGuid();
                    if (existing is null)
                    {
                        database.InboxDigests.Add(new WorkInboxDigestRow
                        {
                            CompanyId = companyId, Id = digestId, RecipientUserId = recipient.Key, SlotUtc = releasedSlot, ReleasedAtUtc = now,
                            ScheduleVersion = schedule.Version,
                        });
                        digests++;
                    }

                    foreach (var item in recipient)
                    {
                        database.InboxDigestItems.Add(new WorkInboxDigestItemRow { CompanyId = companyId, NotificationId = item.Id, DigestId = digestId });
                        items++;
                    }
                }
            }

            RequireCompany(companyId);
            await database.SaveChangesAsync(cancellationToken);
            RequireCompany(companyId);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new WorkCollaborationCycleV1(overdue.Length, digests, items, slot is not null);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private void RequireCompany(Guid expected)
    {
        if (!company.IsResolved || company.CompanyId != expected)
            throw new InvalidOperationException("The company changed during the collaboration cycle.");
    }
}
