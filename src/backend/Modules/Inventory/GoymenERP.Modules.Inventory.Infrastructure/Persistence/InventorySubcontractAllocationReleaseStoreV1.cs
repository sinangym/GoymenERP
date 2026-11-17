using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static GoymenERP.Modules.Inventory.Application.InventorySubcontractDeliveryContractV1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>6957941048: an explicit hold release, independent of dispatch. Existing owner tables only.</summary>
public sealed class InventorySubcontractAllocationReleaseStoreV1(InventoryDbContext db, InventoryStore physical,
    ICompanyContext company, ISalesOrderLineSerializationLockBinderV1 salesLocks, IAuditEventCollector audit,
    IAuditTransactionWriterV1 auditWriter, IRefusedAttemptRecorderV1 refusals, TimeProvider clock)
    : IInventorySubcontractAllocationReleaseStoreV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true };
    private const string Command = InventorySubcontractAllocationReleaseV1.Command;
    private sealed record StoredOutcome(int Status, string? Code, InventorySubcontractAllocationReleaseViewV1? Release);

    public async Task<InventorySubcontractAllocationReleaseCandidatesV1> ListAsync(Guid? after,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        Clean(); await freshAuthority(cancellationToken);
        var query = db.Allocations.AsNoTracking().Where(x => x.DemandKind == InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine
            && x.Status == (int)StockAllocationStatus.Active && x.RemainingKg > 0m);
        if (after is { } cursor) query = query.Where(x => x.Id.CompareTo(cursor) > 0);
        var rows = await query.OrderBy(x => x.Id).Take(51).ToListAsync(cancellationToken);
        await freshAuthority(cancellationToken);
        var page = rows.Take(50).ToArray(); var ids = page.Select(x => x.Id).ToArray(); var labelIds = page.Select(x => x.LabelId).Distinct().ToArray();
        var sources = await db.SubcontractAllocationSources.AsNoTracking().Where(x => ids.Contains(x.AllocationId)).ToListAsync(cancellationToken);
        await freshAuthority(cancellationToken);
        var labels = await db.Labels.AsNoTracking().Where(x => labelIds.Contains(x.Id)).ToListAsync(cancellationToken);
        await freshAuthority(cancellationToken);
        var items = page.Select(row =>
        {
            var source = ValidateSource(row, sources.SingleOrDefault(x => x.AllocationId == row.Id));
            var label = labels.SingleOrDefault(x => x.Id == row.LabelId) ?? throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
            ValidateLabel(row, label, source);
            return new InventorySubcontractAllocationReleaseCandidateV1(View(row), label.Code, TextVersion(label.Version),
                source.CustomerPartyId, source.CustomerAccountId, source.ManufacturingOrderId, source.OutputFactId);
        }).ToArray();
        return new(items, rows.Count > 50 ? page[^1].Id.ToString("D") : null);
    }

    public async Task<InventorySubcontractAllocationReleaseResultV1> ReleaseAsync(Guid allocationId,
        ReleaseInventorySubcontractAllocationV1 request, InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        Clean(); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var expected = Version(request.ExpectedAllocationVersion);
        if (allocationId == Guid.Empty || !Text(request.Reason, 1000) || metadata.PermissionCode != ErpPermissions.InventoryPhysicalAllocationWrite
            || !Text(metadata.ActorId, 128) || !Text(metadata.IdempotencyKey, 128) || !Text(metadata.CorrelationId, 128)
            || !Text(metadata.SourceChannel, 128) || metadata.RoleCodes is null || metadata.RoleCodes.Any(x => !Text(x, 128))
            || metadata.CausationId is not null && !Text(metadata.CausationId, 128)) throw Error(400, InventoryAllocationCodesV1.ValidationFailed);
        var key = Hash(metadata.IdempotencyKey); var payload = Hash(new { allocationId, request });
        await freshAuthority(cancellationToken);
        if (await ReplayAsync(allocationId, key, payload, metadata, freshAuthority, cancellationToken) is { } prior) return prior;
        // Only the immutable demand key is observed before entering the lock hierarchy. No physical/allocation lock precedes Sales.
        var target = await db.Allocations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == allocationId, cancellationToken);
        await freshAuthority(cancellationToken);
        if (target is null || target.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine)
            throw Error(404, "not_found");
        if (target.DemandLineId is not { } demandLine || demandLine == Guid.Empty) throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        StoredOutcome outcome;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var bound = salesLocks.Bind(db.Database.GetDbConnection(), tx.GetDbTransaction());
            await bound.AcquireAsync(demandLine, cancellationToken); await freshAuthority(cancellationToken);
            if (await ReplayAsync(allocationId, key, payload, metadata, freshAuthority, cancellationToken) is { } winner)
            { await tx.CommitAsync(cancellationToken); return winner; }
            await physical.LockPhysicalValidationAsync(cancellationToken); await freshAuthority(cancellationToken);
            var row = await db.Allocations.SingleOrDefaultAsync(x => x.Id == allocationId, cancellationToken);
            await freshAuthority(cancellationToken);
            if (row is null || row.DemandLineId != demandLine || row.DemandKind != target.DemandKind)
                throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
            await db.Entry(row).ReloadAsync(cancellationToken); await freshAuthority(cancellationToken);
            if (row.DemandLineId != demandLine || row.DemandKind != target.DemandKind)
                throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
            var original = await db.SubcontractAllocationSources.AsNoTracking().SingleOrDefaultAsync(x => x.AllocationId == row.Id, cancellationToken);
            await freshAuthority(cancellationToken); var source = ValidateSource(row, original);
            var label = await db.Labels.SingleOrDefaultAsync(x => x.Id == row.LabelId, cancellationToken);
            await freshAuthority(cancellationToken);
            if (label is null) throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
            await db.Entry(label).ReloadAsync(cancellationToken); await freshAuthority(cancellationToken);
            ValidateLabel(row, label, source);
            if (row.Version != expected) outcome = new(409, InventoryAllocationCodesV1.AllocationVersionConflict, null);
            else if (row.Status != (int)StockAllocationStatus.Active) outcome = new(409, InventoryAllocationCodesV1.AllocationStateConflict, null);
            else
            {
                var before = View(row); var position = new StockPosition(company.CompanyId,
                    new((StockPartyKind)row.OwnerKind, row.OwnerId), new((StockPartyKind)row.CustodianKind, row.CustodianId), row.LocationId,
                    new MaterialLabel(label.Id, label.CompanyId, label.Code, label.ProductId, label.LotId, label.ProductVersion), (BlockState)row.Block);
                var allocation = StockAllocation.Restore(row.Id, position, Kilograms.Positive(row.AllocatedKg), Kilograms.Of(row.RemainingKg), row.Version, (StockAllocationStatus)row.Status);
                var consumed = row.AllocatedKg - row.RemainingKg; var released = allocation.Release(expected); var now = clock.GetUtcNow();
                row.RemainingKg = allocation.Remaining.Value; row.Status = (int)allocation.Status; row.Version = allocation.Version;
                row.UpdatedAtUtc = now; row.ReleasedAtUtc = now; row.ReleaseReason = request.Reason;
                label.Version = checked(label.Version + 1); label.UpdatedAtUtc = now;
                var view = new InventorySubcontractAllocationReleaseViewV1(View(row), TextVersion(label.Version), Kg(released.Value), Kg(consumed), request.Reason, now);
                outcome = new(200, null, view);
                audit.Enqueue(new AuditEventDraft(company.CompanyId, InventorySubcontractAllocationReleaseV1.Event, "Inventory.StockAllocation", row.Id.ToString("D"),
                    "Release", expected, row.Version,
                    AuditActorTypes.User, metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes, JsonSerializer.Serialize(before, Json),
                    JsonSerializer.Serialize(view, Json), request.Reason, null, AuditAiInvolvement.None, null,
                    metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            }
            if (outcome.Status == 409)
                refusals.Record(new RefusedAttemptV1 { CompanyId = company.CompanyId, CommandName = Command, Operation = "Release",
                    TargetType = "Inventory.StockAllocation", TargetId = allocationId.ToString("D"), ExpectedVersion = expected,
                    ActorType = AuditActorTypes.User, ActorId = metadata.ActorId, PermissionCode = metadata.PermissionCode, RoleCodes = metadata.RoleCodes.ToArray(),
                    HttpStatus = 409, ProblemCode = outcome.Code!, RefusalClass = outcome.Code == InventoryAllocationCodesV1.AllocationVersionConflict
                        ? RefusedAttemptClassesV1.Concurrency : RefusedAttemptClassesV1.BusinessRule,
                    CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, IdempotencyKey = metadata.IdempotencyKey, SourceChannel = metadata.SourceChannel });
            db.Commands.Add(new InventoryCommandRow { CompanyId = company.CompanyId, CommandName = Command, IdempotencyKeyHash = key,
                PayloadHash = payload, ActorId = metadata.ActorId, TargetId = allocationId, ResultJson = JsonSerializer.Serialize(outcome, Json),
                CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            await freshAuthority(cancellationToken);
            // Flush through Audit's actual borrowed-TX owner before SaveChanges: the legacy Inventory interceptor sees no pending draft.
            await auditWriter.WritePendingAsync(tx.GetDbTransaction(), cancellationToken);
            await freshAuthority(cancellationToken); await db.SaveChangesAsync(cancellationToken); await freshAuthority(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception error)
        {
            auditWriter.DiscardPending();
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
            finally { db.ChangeTracker.Clear(); }
            if (error is SalesOrderLineBusyException busy) throw Error(409, busy.Code);
            if (error is DbUpdateConcurrencyException || error is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }
                || error is SqlException { Number: 1205 }) throw Error(409, InventoryAllocationCodesV1.ConcurrentChange);
            throw;
        }
        return Answer(outcome, metadata.CorrelationId, false);
    }

    private async Task<InventorySubcontractAllocationReleaseResultV1?> ReplayAsync(Guid id, string key, string payload,
        InventoryCommandMetadata metadata, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var row = await db.Commands.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == Command && x.IdempotencyKeyHash == key, token);
        await fence(token); if (row is null) return null;
        if (row.CompanyId != company.CompanyId || row.ActorId != metadata.ActorId || row.TargetId != id || row.PayloadHash != payload)
            throw Error(409, InventoryAllocationCodesV1.IdempotencyConflict);
        if (!Text(row.CorrelationId, 128)) throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        StoredOutcome value;
        try { value = JsonSerializer.Deserialize<StoredOutcome>(row.ResultJson, Json) ?? throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
        catch (JsonException) { throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
        if (value.Release is { } release && (release.Allocation.CompanyId != company.CompanyId || release.Allocation.AllocationId != id
            || release.Allocation.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine
            || release.Allocation.Status != nameof(StockAllocationStatus.Released) || release.Allocation.RemainingKg != "0"
            || !ValidQuantities(release) || !Text(release.ReleaseReason, 1000) || release.ReleasedAtUtc == default
            || release.ReleasedAtUtc.Offset != TimeSpan.Zero || release.Allocation.UpdatedAtUtc != release.ReleasedAtUtc))
            throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        return Answer(value, row.CorrelationId, true);
    }
    private InventorySubcontractDeliverySourceV1 ValidateSource(AllocationRow row, InventorySubcontractAllocationSourceRow? sourceRow)
    {
        if (sourceRow is null || row.CompanyId != company.CompanyId || sourceRow.CompanyId != company.CompanyId || sourceRow.AllocationId != row.Id
            || sourceRow.LabelId != row.LabelId || row.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine)
            throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        InventorySubcontractDeliverySourceV1 source;
        try { source = JsonSerializer.Deserialize<InventorySubcontractDeliverySourceV1>(sourceRow.SourceJson, Json) ?? throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
        catch (JsonException) { throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable); }
        Source(source, company.CompanyId, row.LabelId);
        if (Hash(source) != sourceRow.SourceHash || source.CustomerPartyId != sourceRow.CustomerPartyId || source.CustomerAccountId != sourceRow.CustomerAccountId
            || source.SalesOrderId != sourceRow.SalesOrderId || source.SalesOrderLineId != sourceRow.SalesOrderLineId
            || source.SalesOrderVersion != sourceRow.SalesOrderVersion || source.SalesLineRevision != sourceRow.SalesLineRevision
            || source.ManufacturingOrderId != sourceRow.ManufacturingOrderId || source.ManufacturingOrderVersion != sourceRow.ManufacturingOrderVersion
            || source.ManufacturingPlanId != sourceRow.ManufacturingPlanId || source.ManufacturingPlanVersion != sourceRow.ManufacturingPlanVersion
            || source.OutputFactId != sourceRow.OutputFactId || Quantity(source.OutputNetKg).Value != sourceRow.OutputNetKg
            || row.DemandId != source.SalesOrderId || row.DemandLineId != source.SalesOrderLineId
            || row.DemandProductId != source.ProductId || row.DemandProductVersion != source.ProductVersion)
            throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        return source;
    }
    private void ValidateLabel(AllocationRow row, LabelRow label, InventorySubcontractDeliverySourceV1 source)
    {
        if (label.CompanyId != company.CompanyId || label.Id != row.LabelId || label.ProductId != source.ProductId
            || label.ProductVersion != source.ProductVersion || !Text(label.Code, 200) || label.Version < 1
            || !InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld((StockPartyKind)row.OwnerKind, row.OwnerId,
                (StockPartyKind)row.CustodianKind, row.CustodianId, company.CompanyId, source.CustomerPartyId)
            || row.AllocatedKg <= 0m || row.RemainingKg < 0m || row.RemainingKg > row.AllocatedKg || row.Version < 1
            || !Enum.IsDefined((StockAllocationStatus)row.Status)
            || row.Status == (int)StockAllocationStatus.Active && row.RemainingKg == 0m
            || row.Status != (int)StockAllocationStatus.Active && row.RemainingKg != 0m)
            throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
    }
    private void Clean()
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || db.Database.CurrentTransaction is not null
            || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
    }
    private static InventorySubcontractAllocationReleaseResultV1 Answer(StoredOutcome outcome, string correlation, bool replay)
    {
        if (outcome.Status == 409 && outcome.Release is null && outcome.Code is InventoryAllocationCodesV1.AllocationVersionConflict or InventoryAllocationCodesV1.AllocationStateConflict)
            throw new InventoryAllocationException(409, outcome.Code, replay, correlation);
        if (outcome.Status != 200 || outcome.Code is not null || outcome.Release is null) throw Error(503, InventorySubcontractAllocationReleaseV1.Unavailable);
        return new(outcome.Release, replay, correlation);
    }
    private static InventoryPhysicalAllocationViewV1 View(AllocationRow row) => new(row.Id, row.CompanyId, TextVersion(row.Version),
        ((StockAllocationStatus)row.Status).ToString(), row.LabelId, row.DemandProductId!.Value, row.DemandKind!, row.DemandId!.Value,
        row.DemandLineId!.Value, row.DemandProductVersion!, Kg(row.AllocatedKg), Kg(row.RemainingKg), row.Reason!, row.ReservedUntilUtc,
        row.CreatedAtUtc, row.UpdatedAtUtc, row.IsSubstitute);
    private static bool ValidQuantities(InventorySubcontractAllocationReleaseViewV1 view)
    {
        try
        {
            var released = Quantity(view.ReleasedKg).Value; var allocated = Quantity(view.Allocation.AllocatedKg).Value;
            if (!decimal.TryParse(view.PreviouslyConsumedKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var consumed)
                || consumed < 0m || Kg(consumed) != view.PreviouslyConsumedKg || consumed + released != allocated) return false;
            _ = Version(view.LabelVersion); _ = Version(view.Allocation.Version); return true;
        }
        catch (Exception error) when (error is InventoryDefiniteShipmentException or OverflowException) { return false; }
    }
    private static string Kg(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string TextVersion(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static InventoryAllocationException Error(int status, string code) => new(status, code);
}
