using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingStore : IManufacturingWorkCenterQueueOrderingStore
{
    private ManufacturingCommandMetadata? queueMutationMetadata;

    public async Task<PlanWorkCenterQueueViewV1> MoveQueuePlanAsync(Guid targetPlanId, long expectedVersion,
        long expectedQueueRevision, Guid movingPlanId, Guid? beforePlanId, string reason, ManufacturingCommandMetadata metadata,
        Func<CancellationToken, Task<ManufacturingApiResult<object>?>> finalFence, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Manuel sıra komutu kendi Serializable işlemini açmalıdır.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var plan = await FindPlanAsync(targetPlanId, cancellationToken)
                ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
            if (plan.Version != expectedVersion) throw new ManufacturingVersionConflictException(expectedVersion, plan.Version);
            var queue = await ReadWorkCenterQueueAsync(plan.WorkCenterId, targetPlanId, cancellationToken);
            if (queue.QueueRevision != expectedQueueRevision) throw new ManufacturingQueueVersionException(expectedQueueRevision, queue.QueueRevision);
            var current = ProductionWorkCenterQueueOrderV1.Realized(queue.Queued, queue.Target, queue.TargetIsQueued, queue.StoredOrder)
                .Where(entry => entry.PlanId != targetPlanId || queue.TargetIsQueued).Select(entry => entry.PlanId).ToArray();
            var moved = ProductionWorkCenterQueueOrderV1.Move(current, movingPlanId, beforePlanId);
            if (await finalFence(cancellationToken) is { } refused) throw new ManufacturingQueueFenceException(refused);
            var revision = checked(queue.QueueRevision + 1);
            AppendQueueSnapshot(companyId, plan.WorkCenterId, revision, moved, true, reason.Trim(), metadata);
            var afterQueue = queue with { QueueRevision = revision, StoredOrder = moved };
            var view = ManufacturingQueueViewsV1.Create(companyId, plan.WorkCenterId, targetPlanId, plan.Version, afterQueue);
            Audit(metadata, companyId, "Manufacturing.WorkCenterQueue", plan.WorkCenterId, "manufacturing.queue.moved.v1",
                "Reorder", queue.QueueRevision, revision, JsonSerializer.Serialize(current, Json), JsonSerializer.Serialize(moved, Json), reason.Trim());
            Complete(metadata, companyId, view);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return view;
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw Refused(ManufacturingStoreCodes.ConcurrentChange, "Kuyruk eşzamanlı değişti; güncel sırayı yeniden okuyun.", exception);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ManufacturingWorkCenterQueue> WithManualQueueAsync(Guid workCenterId, ManufacturingWorkCenterQueue queue,
        CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        var version = await dbContext.WorkCenterQueueVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.WorkCenterId == workCenterId)
            .OrderByDescending(row => row.Revision).FirstOrDefaultAsync(cancellationToken);
        if (version is null) return queue;
        var entries = await dbContext.WorkCenterQueueEntries.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.WorkCenterId == workCenterId && row.Revision == version.Revision)
            .OrderBy(row => row.Sequence).Select(row => row.PlanId).ToArrayAsync(cancellationToken);
        var result = queue with { QueueRevision = version.Revision, StoredOrder = entries };
        // A separately captured source/snapshot may have raced: never silently show an incomplete sequence.
        _ = ProductionWorkCenterQueueOrderV1.Realized(result.Queued, result.Target, result.TargetIsQueued, result.StoredOrder);
        return result;
    }

    private void AppendQueueSnapshot(Guid companyId, Guid workCenterId, long revision, IReadOnlyList<Guid> plans,
        bool isManual, string reason, ManufacturingCommandMetadata metadata)
    {
        dbContext.WorkCenterQueueVersions.Add(new WorkCenterQueueVersionRow
        {
            CompanyId = companyId, WorkCenterId = workCenterId, Revision = revision, IsManual = isManual,
            RecordedBy = metadata.ActorId.Trim(), RecordedAtUtc = timeProvider.GetUtcNow(), Reason = reason,
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        for (var index = 0; index < plans.Count; index++)
            dbContext.WorkCenterQueueEntries.Add(new WorkCenterQueueEntryRow
            {
                CompanyId = companyId, WorkCenterId = workCenterId, Revision = revision,
                Sequence = index + 1, PlanId = plans[index],
            });
    }

    /// <summary>Runs after business SaveChanges, before the enclosing owner/host transaction commits.</summary>
    private async Task SynchronizeManualQueuesAsync(ManufacturingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        var latest = await dbContext.WorkCenterQueueVersions.AsNoTracking().Where(row => row.CompanyId == companyId)
            .GroupBy(row => row.WorkCenterId).Select(group => new { WorkCenterId = group.Key, Revision = group.Max(row => row.Revision) })
            .ToListAsync(cancellationToken);
        if (latest.Count == 0) return;
        var eligible = await ReadEligibleQueueKeysAsync(companyId, cancellationToken);
        foreach (var version in latest.OrderBy(row => row.WorkCenterId))
        {
            var current = await dbContext.WorkCenterQueueEntries.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.WorkCenterId == version.WorkCenterId && row.Revision == version.Revision)
                .OrderBy(row => row.Sequence).Select(row => row.PlanId).ToArrayAsync(cancellationToken);
            var keys = eligible.GetValueOrDefault(version.WorkCenterId) ?? [];
            var next = ProductionWorkCenterQueueOrderV1.Reconcile(current, keys);
            if (next.SequenceEqual(current)) continue;
            var revision = checked(version.Revision + 1);
            const string reason = "Kuyruk üyeliği değişti; mevcut sıra korundu.";
            AppendQueueSnapshot(companyId, version.WorkCenterId, revision, next, false, reason, metadata);
            Audit(metadata, companyId, "Manufacturing.WorkCenterQueue", version.WorkCenterId, "manufacturing.queue.membership-changed.v1",
                "Synchronize", version.Revision, revision, JsonSerializer.Serialize(current, Json), JsonSerializer.Serialize(next, Json), reason);
        }
    }

    private async Task<Dictionary<Guid, IReadOnlyList<ProductionQueueAutomaticKeyV1>>> ReadEligibleQueueKeysAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        var plans = await dbContext.Plans.AsNoTracking().Where(row => row.CompanyId == companyId && row.Status == (int)PlanStatus.Approved
                && !dbContext.Orders.Any(order => order.CompanyId == companyId && order.PlanId == row.Id && order.Status == (int)OrderStatus.Closed))
            .Select(row => new { row.Id, row.Version, row.WorkCenterId, row.RequestedDueDate, row.CreatedAtUtc }).ToListAsync(cancellationToken);
        if (plans.Count == 0) return [];
        var ids = plans.Select(row => row.Id).ToArray();
        var revisions = await dbContext.PlanRevisions.AsNoTracking().Where(row => row.CompanyId == companyId && ids.Contains(row.PlanId))
            .Select(row => new { row.PlanId, row.PlanVersion, row.RevisionNo, row.WorkCenterId }).ToListAsync(cancellationToken);
        var orders = await dbContext.Orders.AsNoTracking().Where(row => row.CompanyId == companyId && ids.Contains(row.PlanId)
                && row.Status != (int)OrderStatus.Cancelled).Select(row => new { row.PlanId, row.DueDate }).ToListAsync(cancellationToken);
        var priorities = await dbContext.PlanPriorityVersions.AsNoTracking().Where(row => row.CompanyId == companyId && ids.Contains(row.PlanId)
                && row.Revision == dbContext.PlanPriorityVersions.Where(other => other.CompanyId == companyId && other.PlanId == row.PlanId)
                    .Max(other => other.Revision)).ToDictionaryAsync(row => row.PlanId, cancellationToken);
        return plans.Select(plan =>
        {
            var revision = revisions.Where(row => row.PlanId == plan.Id && row.PlanVersion <= plan.Version).MaxBy(row => row.RevisionNo);
            var order = orders.SingleOrDefault(row => row.PlanId == plan.Id);
            priorities.TryGetValue(plan.Id, out var priority);
            return (Center: revision?.WorkCenterId ?? plan.WorkCenterId, Key: new ProductionQueueAutomaticKeyV1(plan.Id,
                order is null ? plan.RequestedDueDate : order.DueDate, priority?.Score ?? 0, plan.CreatedAtUtc));
        }).GroupBy(item => item.Center).ToDictionary(group => group.Key,
            group => (IReadOnlyList<ProductionQueueAutomaticKeyV1>)group.Select(item => item.Key).ToArray());
    }

    private async Task SaveWithManualQueuesAsync(CancellationToken cancellationToken)
    {
        var metadata = queueMutationMetadata;
        if (metadata is null) { await PersistChangesAsync(cancellationToken); return; }
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        try
        {
            await PersistChangesAsync(cancellationToken);
            await SynchronizeManualQueuesAsync(metadata, cancellationToken);
            if (dbContext.ChangeTracker.HasChanges()) await PersistChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 1205 or 1222)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw Refused(ManufacturingStoreCodes.ConcurrentChange, "Kuyruk eşzamanlı değişti; işlem kaydedilmedi.", exception);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
        finally { queueMutationMetadata = null; }
    }
}
