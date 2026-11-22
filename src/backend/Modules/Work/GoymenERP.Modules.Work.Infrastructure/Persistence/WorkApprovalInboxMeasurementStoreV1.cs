using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Work.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>
/// W1-A: Work's own inbox measurement events (append-only, company-scoped, no actor). The owners' pending cycles reach
/// it through the host; it reads and writes only <c>work.ApprovalInboxEvents</c>.
/// </summary>
public sealed class WorkApprovalInboxMeasurementStoreV1(WorkDbContext database, ICompanyContext company, TimeProvider time)
    : IWorkApprovalInboxMeasurementsV1
{
    /// <summary>A racing reader can insert the same arrival first; one fresh retry settles it.</summary>
    private const int ObservationAttempts = 2;

    public async Task ObserveAsync(
        Guid companyId,
        string kind,
        IReadOnlyList<WorkApprovalInboxCycleV1> pending,
        bool complete,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        RequireCompany(companyId);
        RequireKind(kind);
        foreach (var cycle in pending)
        {
            if (cycle is null || cycle.ItemId == Guid.Empty || cycle.WaitingSinceUtc == default
                || cycle.WaitingSinceUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Inbox cycles need an item id and a UTC waiting time.", nameof(pending));
            }
        }

        for (var attempt = 1; attempt <= ObservationAttempts; attempt++)
        {
            database.ChangeTracker.Clear();
            var open = await OpenCyclesAsync(companyId, kind, cancellationToken);
            RequireCompany(companyId);
            var now = time.GetUtcNow();
            var current = pending.Select(cycle => (cycle.ItemId, cycle.WaitingSinceUtc)).ToHashSet();
            var added = false;
            // A waiting time ahead of this server's clock is not recorded yet (the next read records it).
            foreach (var cycle in current.Where(cycle => !open.Contains(cycle) && cycle.WaitingSinceUtc <= now))
            {
                if (await database.ApprovalInboxEvents.AnyAsync(row => row.CompanyId == companyId && row.Kind == kind
                        && row.ItemId == cycle.ItemId && row.CycleStartedAtUtc == cycle.WaitingSinceUtc
                        && row.EventType == (int)WorkApprovalInboxEventTypeV1.Arrived, cancellationToken))
                {
                    // Already resolved once and listed again with the same time: the owner reopened nothing new.
                    continue;
                }
                database.ApprovalInboxEvents.Add(Row(companyId, kind, cycle.ItemId, cycle.WaitingSinceUtc,
                    WorkApprovalInboxEventTypeV1.Arrived, cycle.WaitingSinceUtc, now, null));
                added = true;
            }
            if (complete)
            {
                foreach (var cycle in open.Where(cycle => !current.Contains(cycle)))
                {
                    database.ApprovalInboxEvents.Add(Row(companyId, kind, cycle.ItemId, cycle.WaitingSinceUtc,
                        WorkApprovalInboxEventTypeV1.ResolvedObserved, now, now, null));
                    added = true;
                }
            }
            if (!added)
            {
                return;
            }

            try
            {
                RequireCompany(companyId);
                await database.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < ObservationAttempts && !cancellationToken.IsCancellationRequested)
            {
                // Another reader recorded the same cycle fact first; read again and add only what is still missing.
            }
            finally
            {
                database.ChangeTracker.Clear();
            }
        }
    }

    public async Task<WorkApprovalInboxInteractionOutcomeV1> RecordInteractionAsync(
        Guid companyId,
        WorkApprovalInboxInteractionV1 interaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        RequireCompany(companyId);
        RequireKind(interaction.Kind);
        if (interaction.ClientEventId == Guid.Empty || interaction.ItemId == Guid.Empty
            || interaction.WaitingSinceUtc == default || interaction.WaitingSinceUtc.Offset != TimeSpan.Zero
            || interaction.EventType is not (WorkApprovalInboxEventTypeV1.Opened or WorkApprovalInboxEventTypeV1.OwnerScreenOpened))
        {
            throw new ArgumentException("An inbox interaction needs a client event id, an item, a UTC cycle and an interaction type.",
                nameof(interaction));
        }

        database.ChangeTracker.Clear();
        var existing = await FindInteractionAsync(companyId, interaction.ClientEventId, cancellationToken);
        RequireCompany(companyId);
        if (existing is not null)
        {
            return Same(existing, interaction) ? WorkApprovalInboxInteractionOutcomeV1.Replayed : WorkApprovalInboxInteractionOutcomeV1.Conflict;
        }

        var now = time.GetUtcNow();
        if (interaction.WaitingSinceUtc > now)
        {
            return WorkApprovalInboxInteractionOutcomeV1.Conflict;
        }
        database.ApprovalInboxEvents.Add(Row(companyId, interaction.Kind, interaction.ItemId, interaction.WaitingSinceUtc,
            interaction.EventType, now, now, interaction.ClientEventId));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return WorkApprovalInboxInteractionOutcomeV1.Recorded;
        }
        catch (DbUpdateException) when (!cancellationToken.IsCancellationRequested)
        {
            database.ChangeTracker.Clear();
            existing = await FindInteractionAsync(companyId, interaction.ClientEventId, cancellationToken);
            if (existing is null)
            {
                throw;
            }
            return Same(existing, interaction) ? WorkApprovalInboxInteractionOutcomeV1.Replayed : WorkApprovalInboxInteractionOutcomeV1.Conflict;
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private async Task<HashSet<(Guid ItemId, DateTimeOffset WaitingSinceUtc)>> OpenCyclesAsync(
        Guid companyId, string kind, CancellationToken cancellationToken)
    {
        var arrived = (int)WorkApprovalInboxEventTypeV1.Arrived;
        var resolved = (int)WorkApprovalInboxEventTypeV1.ResolvedObserved;
        var rows = await database.ApprovalInboxEvents.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Kind == kind && row.EventType == arrived
                && !database.ApprovalInboxEvents.Any(other => other.CompanyId == row.CompanyId && other.Kind == row.Kind
                    && other.ItemId == row.ItemId && other.CycleStartedAtUtc == row.CycleStartedAtUtc && other.EventType == resolved))
            .Select(row => new { row.ItemId, row.CycleStartedAtUtc })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.ItemId, row.CycleStartedAtUtc)).ToHashSet();
    }

    private Task<WorkApprovalInboxEventRow?> FindInteractionAsync(Guid companyId, Guid clientEventId, CancellationToken cancellationToken) =>
        database.ApprovalInboxEvents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == companyId && row.ClientEventId == clientEventId, cancellationToken);

    private static bool Same(WorkApprovalInboxEventRow row, WorkApprovalInboxInteractionV1 interaction) =>
        string.Equals(row.Kind, interaction.Kind, StringComparison.Ordinal)
        && row.ItemId == interaction.ItemId
        && row.CycleStartedAtUtc == interaction.WaitingSinceUtc
        && row.EventType == (int)interaction.EventType;

    private static WorkApprovalInboxEventRow Row(Guid companyId, string kind, Guid itemId, DateTimeOffset cycleStart,
        WorkApprovalInboxEventTypeV1 type, DateTimeOffset occurredAt, DateTimeOffset recordedAt, Guid? clientEventId) =>
        new()
        {
            CompanyId = companyId,
            Id = Guid.NewGuid(),
            Kind = kind,
            ItemId = itemId,
            CycleStartedAtUtc = cycleStart,
            EventType = (int)type,
            OccurredAtUtc = occurredAt,
            RecordedAtUtc = recordedAt,
            ClientEventId = clientEventId,
        };

    private static void RequireKind(string kind)
    {
        var separator = kind?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (separator <= 0 || !NativeApprovalQueueRulesV1.IsValidKind(kind![..separator], kind))
        {
            throw new ArgumentException("The inbox kind is not a valid approval kind.", nameof(kind));
        }
    }

    private void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId)
        {
            throw new InvalidOperationException("Work inbox measurements require the active company context.");
        }
    }
}
