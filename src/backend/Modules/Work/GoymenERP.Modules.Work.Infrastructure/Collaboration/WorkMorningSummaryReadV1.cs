using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Collaboration;

/// <summary>WP-C5 first read slice. Settings/YOL-D25 and the existing scheduler own the morning slot and its release.
/// Reads the actual recipient digest, never a role-wide data query or an AI answer. The existing scheduler stays OFF by default.</summary>
public sealed class WorkMorningSummaryReadV1(WorkDbContext database, ICompanyContext company, ICurrentUser user,
    IWorkPersonDirectoryV1 people, IWorkRecordAccessPortV1 records, IWorkDigestScheduleReaderV1 schedules, TimeProvider time)
    : IWorkMorningSummaryReadV1
{
    public async Task<WorkMorningSummaryV1?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var scope = Capture();
        await MemberAsync(scope, cancellationToken);
        var now = time.GetUtcNow();
        Fence(scope);
        var schedule = await schedules.ReadAsync(cancellationToken);
        Fence(scope);
        var slot = MorningSlot(schedule, scope.CompanyId, now) ?? throw Unavailable();
        var first = await ReadSourceAsync(scope, slot, cancellationToken);
        await CheckItemsAsync(scope, first, cancellationToken);
        await MemberAsync(scope, cancellationToken);
        Fence(scope);
        var currentSchedule = await schedules.ReadAsync(cancellationToken);
        Fence(scope);
        if (MorningSlot(currentSchedule, scope.CompanyId, now) != slot || currentSchedule.Version != schedule.Version)
            throw Unavailable();
        var second = await ReadSourceAsync(scope, slot, cancellationToken);
        if (first.Digest != second.Digest || !first.Items.SequenceEqual(second.Items)) throw Unavailable();
        // Revalidate every owner's read after the last source await; a removed grant must not release cached counts.
        await CheckItemsAsync(scope, second, cancellationToken);
        await MemberAsync(scope, cancellationToken);
        Fence(scope);
        if (second.Digest is not { } digest) return null;
        if (digest.ScheduleVersion != schedule.Version) throw Unavailable();
        return new(scope.CompanyId, digest.Id, digest.SlotUtc, digest.ScheduleVersion,
            second.Items.GroupBy(item => item.Kind).OrderBy(group => group.Key)
                .Select(group => new WorkMorningSummaryCountV1(
                    WorkInboxNotificationClassesV1.Wire((WorkInboxNotificationKindV1)group.Key), group.Count())).ToArray());
    }

    private static DateTimeOffset? MorningSlot(WorkDigestScheduleV1? schedule, Guid companyId, DateTimeOffset now) =>
        schedule is { Available: true } && schedule.CompanyId == companyId && schedule.LocalTimes.Count > 0
            && !string.IsNullOrWhiteSpace(schedule.Version)
            ? WorkDigestSlotsV1.LatestSlotAtOrBefore(schedule with { LocalTimes = [schedule.LocalTimes.Min()] }, now) : null;

    private async Task<Source> ReadSourceAsync(Scope scope, DateTimeOffset slot, CancellationToken token)
    {
        Fence(scope);
        var digest = await database.InboxDigests.AsNoTracking()
            .Where(row => row.CompanyId == scope.CompanyId && row.RecipientUserId == scope.Actor && row.SlotUtc == slot)
            .Select(row => new Digest(row.Id, row.SlotUtc, row.ScheduleVersion)).SingleOrDefaultAsync(token);
        Fence(scope);
        if (digest is null) return new(null, []);
        var items = await (from link in database.InboxDigestItems.AsNoTracking()
            join item in database.InboxNotifications.AsNoTracking()
                on new { link.CompanyId, Id = link.NotificationId } equals new { item.CompanyId, item.Id }
            where link.CompanyId == scope.CompanyId && link.DigestId == digest.Id
            orderby item.Id
            select new Item(item.Id, item.RecipientUserId, item.Kind, item.Class, item.SourceId, item.RecordType, item.RecordId))
            .Take(WorkCollaborationLimitsV1.PageSize + 1).ToArrayAsync(token);
        Fence(scope);
        if (items.Length > WorkCollaborationLimitsV1.PageSize) throw Unavailable();
        return new(digest, items);
    }

    private async Task CheckItemsAsync(Scope scope, Source source, CancellationToken token)
    {
        foreach (var item in source.Items)
        {
            Fence(scope);
            if (item.Recipient != scope.Actor || item.Class != (int)WorkInboxNotificationClassV1.Digest
                || !Enum.IsDefined((WorkInboxNotificationKindV1)item.Kind)
                || WorkInboxNotificationClassesV1.Of((WorkInboxNotificationKindV1)item.Kind) != WorkInboxNotificationClassV1.Digest)
                throw Unavailable();
            if (item.RecordType is { } type && item.RecordId is { } id && id != Guid.Empty
                && Enum.IsDefined((WorkRecordTypeV1)type))
            {
                var access = await records.CheckCurrentActorAsync(scope.CompanyId, new((WorkRecordTypeV1)type, id), token);
                Fence(scope);
                if (access == WorkRecordAccessOutcomeV1.NotReadable) throw Denied();
                if (access != WorkRecordAccessOutcomeV1.Readable) throw Unavailable();
            }
            else if (item.RecordType is null && item.RecordId is null && item.Kind == (int)WorkInboxNotificationKindV1.TaskAssigned)
            {
                // Native personal-task read is active membership + exact assignee; it has no record-owner grant.
                var personal = await database.Tasks.AsNoTracking().AnyAsync(task => task.CompanyId == scope.CompanyId
                    && task.Id == item.SourceId && task.AssigneeUserId == scope.Actor
                    && task.RecordType == null && task.RecordId == null, token);
                Fence(scope);
                if (!personal) throw Unavailable();
            }
            else throw Unavailable(); // Event-only sources need an authoritative current owner read before this slice can include them.
        }
    }

    private async Task MemberAsync(Scope scope, CancellationToken token)
    {
        Fence(scope);
        var membership = await people.IsActiveMemberAsync(scope.CompanyId, scope.Actor, token);
        Fence(scope);
        if (membership == WorkPersonOutcomeV1.Denied) throw Denied();
        if (membership != WorkPersonOutcomeV1.Found) throw Unavailable();
    }

    private Scope Capture()
    {
        if (!user.IsAuthenticated || !Guid.TryParse(user.UserId, out var actor) || actor == Guid.Empty)
            throw new WorkCollaborationException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw Denied();
        return new(company.CompanyId, actor, user.UserId!);
    }

    private void Fence(Scope scope)
    {
        if (!user.IsAuthenticated || user.UserId != scope.ActorId || !company.IsResolved
            || company.CompanyId != scope.CompanyId || !user.CanAccessCompany(scope.CompanyId)) throw Denied();
    }

    private static WorkCollaborationException Denied() => new(403, "forbidden");
    private static WorkCollaborationException Unavailable() => new(503, "service_unavailable");
    private sealed record Scope(Guid CompanyId, Guid Actor, string ActorId);
    private sealed record Digest(Guid Id, DateTimeOffset SlotUtc, string ScheduleVersion);
    private sealed record Item(Guid Id, Guid Recipient, int Kind, int Class, Guid SourceId, int? RecordType, Guid? RecordId);
    private sealed record Source(Digest? Digest, IReadOnlyList<Item> Items);
}
