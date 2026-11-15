using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §3.2/§3.3 (F017-b): the module-owned create and release of future supply allocations. No route and no permission
/// are declared here (F017-b2); the host authorises first and hands a fence that re-checks the same authority after the locks
/// and right before the save. Each call is ONE transaction on Inventory's connection: idempotency replay → Sales line lock →
/// purchase line lock → fence → source reads → checks → allocation + ledger + command + audit + outbox → one save → commit.
/// <para>
/// Terminal refusals (STK-S14 technical item, SINAN-CEVAP 27 application note): a 409 state refusal or a 422 rule refusal decided
/// under the locks is recorded in the same idempotency command row (status + code, no allocation) with its own audit event and no
/// allocation, ledger or outbox change; the same key replays the refusal, a different payload under the key is 409
/// idempotency_conflict. Shape refusals (400), a missing reference (404), an unreadable source (503), a busy lock and a lost race
/// write nothing and may be retried with the same key.
/// </para>
/// <para>
/// KLN-D65 / KLN-D66 / KLN-D67: physical + future + this request stays within the Sales line's open quantity
/// (<see cref="InventoryAllocationDemandCap"/>, the same check the physical allocation path makes under the same Sales line lock);
/// a different unit is compared in kilograms, and the product identity (not the card revision) must match.
/// </para>
/// </summary>
public sealed class InventoryFutureAllocationWriter(
    InventoryDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    ISalesOrderLineSerializationLockBinderV1 salesLineLocks,
    IPurchaseLineSerializationLockBinderV1 purchaseLineLocks,
    IInventoryFutureSupplySourceV1 supplySource,
    IInventoryFutureDemandSourceV1 demandSource,
    IInventoryFutureUnitConverterV1 unitConverter,
    TimeProvider timeProvider) : IInventoryFutureAllocationWriterV1
{
    public async Task<InventoryFutureAllocationResultV1> CreateAsync(
        CreateFutureSupplyAllocationV1 request,
        InventoryCommandMetadata metadata,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fence);
        Engine.RequireMetadata(metadata);
        Engine.RequireCanonicalKeys(metadata);
        var companyId = RequireCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (request.AllocationId == Guid.Empty || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty
            || request.PurchaseOrderId == Guid.Empty || request.PurchaseOrderLineId == Guid.Empty || request.UnitId == Guid.Empty
            || !InventoryFutureAllocationTextV1.TryPositiveQuantity(request.QuantityText, out var requested))
        {
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }

        string reason;
        try
        {
            reason = FutureSupplyAllocation.RequireReason(request.Reason);
        }
        catch (FutureSupplyRuleException exception)
        {
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed, exception);
        }

        var keyHash = Engine.KeyHash(metadata.IdempotencyKey);
        var payloadHash = Engine.PayloadHash(new
        {
            request.AllocationId,
            request.SalesOrderId,
            request.SalesOrderLineId,
            request.PurchaseOrderId,
            request.PurchaseOrderLineId,
            quantity = requested.ToString(),
            request.UnitId,
            request.Reason,
        });
        if (await FindReplayAsync(InventoryFutureAllocationCommandsV1.Create, keyHash, payloadHash, metadata.ActorId,
                cancellationToken) is { } replay)
        {
            return replay;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            return await CreateLockedAsync(transaction, request, requested, reason, metadata, fence, companyId, keyHash,
                payloadHash, cancellationToken);
        }
        catch (InventoryAllocationException refusal) when (!refusal.IsReplay && RecordableCreateRefusals.Contains(
            (refusal.Status, refusal.Code)))
        {
            return await RecordRefusalAsync(transaction, fence, InventoryFutureAllocationCommandsV1.Create, keyHash, payloadHash,
                metadata, companyId, request.AllocationId, refusal, CreateRefusedEventType, CreateRefusedOperation, reason,
                cancellationToken);
        }
    }

    private async Task<InventoryFutureAllocationResultV1> CreateLockedAsync(
        IDbContextTransaction transaction,
        CreateFutureSupplyAllocationV1 request,
        FutureSupplyQuantity requested,
        string reason,
        InventoryCommandMetadata metadata,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        Guid companyId,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();
        await AcquireSalesLineAsync(connection, dbTransaction, request.SalesOrderLineId, cancellationToken);
        await AcquirePurchaseLineAsync(connection, dbTransaction, request.PurchaseOrderLineId, cancellationToken);
        await fence.RequireStillAsync(cancellationToken);

        var demand = await ReadDemandAsync(request.SalesOrderId, request.SalesOrderLineId, cancellationToken);
        var supply = await ReadSupplyAsync(request.PurchaseOrderId, request.PurchaseOrderLineId, cancellationToken);
        // KLN-D67: the product identity decides; the Sales and purchase card revisions may differ (each row keeps its own).
        if (supply.ProductId != demand.ProductId)
        {
            throw Engine.Error(422, InventoryFutureAllocationCodesV1.InvalidReference);
        }

        if (!InventoryFutureAllocationTextV1.TryNonNegativeQuantity(supply.AllocatableText, out var allocatable))
        {
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }

        if (await dbContext.FutureSupplyAllocations.AsNoTracking()
                .AnyAsync(row => row.Id == request.AllocationId, cancellationToken))
        {
            throw Engine.Error(409, InventoryFutureAllocationCodesV1.StateConflict);
        }

        // SAT-D298: the request unit is converted once to the purchase line's own unit; no conversion source → 422, never a guess.
        var quantity = await ToPurchaseUnitAsync(supply, requested, request.UnitId, cancellationToken);
        var (ledgerRow, ledger) = await LoadLedgerAsync(companyId, supply, cancellationToken);
        try
        {
            ledger.Reserve(allocatable, quantity);
        }
        catch (FutureSupplyRuleException exception) when (exception.Failure == FutureSupplyRuleFailure.InsufficientFutureSupply)
        {
            throw Engine.Error(409, InventoryFutureAllocationCodesV1.InsufficientFutureSupply, exception);
        }
        catch (FutureSupplyRuleException exception)
        {
            throw Engine.Error(422, InventoryFutureAllocationCodesV1.ValidationFailed, exception);
        }

        await RequireDemandRoomAsync(demand, quantity, supply.UnitId, cancellationToken);

        var allocation = FutureSupplyAllocation.Create(request.AllocationId, companyId, quantity);
        var now = timeProvider.GetUtcNow();
        if (ledgerRow is null)
        {
            dbContext.FutureSupplyLines.Add(new FutureSupplyLineRow
            {
                CompanyId = companyId,
                PurchaseOrderId = supply.OrderId,
                PurchaseOrderLineId = supply.LineId,
                PurchaseUnitId = supply.UnitId,
                AllocatableSnapshot = ledger.AllocatableSnapshot.Value,
                ActiveAllocated = ledger.ActiveAllocated.Value,
                Version = ledger.Version,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            ledgerRow.AllocatableSnapshot = ledger.AllocatableSnapshot.Value;
            ledgerRow.ActiveAllocated = ledger.ActiveAllocated.Value;
            ledgerRow.Version = ledger.Version;
            ledgerRow.UpdatedAtUtc = now;
        }

        var row = new FutureSupplyAllocationRow
        {
            Id = allocation.Id,
            CompanyId = companyId,
            SourceKind = InventoryFutureAllocationKindsV1.PurchaseOrderLine,
            PurchaseOrderId = supply.OrderId,
            PurchaseOrderLineId = supply.LineId,
            PurchaseUnitId = supply.UnitId,
            ProductId = supply.ProductId,
            ProductVersion = supply.ProductVersion,
            DemandKind = InventoryFutureAllocationKindsV1.SalesOrderLine,
            DemandId = demand.OrderId,
            DemandLineId = demand.LineId,
            AllocatedQuantity = allocation.Allocated.Value,
            RemainingQuantity = allocation.Remaining.Value,
            ReleasedQuantity = allocation.Released.Value,
            ManualSequence = null,
            Reason = reason,
            Status = (int)allocation.Status,
            Version = allocation.Version,
            ObservedPurchaseOrderVersion = supply.ObservedOrderVersion,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.FutureSupplyAllocations.Add(row);
        var view = Engine.View(row);
        dbContext.Commands.Add(CommandRow(companyId, InventoryFutureAllocationCommandsV1.Create, keyHash, payloadHash, metadata,
            row.Id, view, now));
        Engine.Audit(auditEvents, metadata, companyId, row.Id, InventoryFutureAllocationCreatedOutboxV1.EventType,
            Engine.CreatedOperation, null, row.Version, "{}", JsonSerializer.Serialize(view, Engine.Json), reason);
        Engine.AddCreatedOutbox(dbContext, row, metadata, now);

        return await SaveAndCommitAsync(transaction, fence, InventoryFutureAllocationCommandsV1.Create, keyHash, payloadHash,
            metadata, view, cancellationToken);
    }

    public async Task<InventoryFutureAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleaseFutureSupplyAllocationV1 request,
        InventoryCommandMetadata metadata,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fence);
        Engine.RequireMetadata(metadata);
        Engine.RequireCanonicalKeys(metadata);
        var companyId = RequireCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (allocationId == Guid.Empty
            || !InventoryFutureAllocationTextV1.TryVersion(request.ExpectedAllocationVersion, out var expectedVersion))
        {
            throw Engine.Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }

        var (quantity, reasonCode, reasonText) = Engine.ParseRelease(request.QuantityText, request.ReasonCode, request.ReasonText);
        var keyHash = Engine.KeyHash(metadata.IdempotencyKey);
        var payloadHash = Engine.PayloadHash(new
        {
            allocationId,
            expectedAllocationVersion = expectedVersion,
            quantity = quantity?.ToString(),
            request.ReasonCode,
            request.ReasonText,
        });
        if (await FindReplayAsync(InventoryFutureAllocationCommandsV1.Release, keyHash, payloadHash, metadata.ActorId,
                cancellationToken) is { } replay)
        {
            return replay;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            return await ReleaseLockedAsync(transaction, allocationId, expectedVersion, quantity, reasonCode, reasonText,
                metadata, fence, companyId, keyHash, payloadHash, cancellationToken);
        }
        catch (InventoryAllocationException refusal) when (!refusal.IsReplay && RecordableReleaseRefusals.Contains(
            (refusal.Status, refusal.Code)))
        {
            return await RecordRefusalAsync(transaction, fence, InventoryFutureAllocationCommandsV1.Release, keyHash, payloadHash,
                metadata, companyId, allocationId, refusal, ReleaseRefusedEventType, ReleaseRefusedOperation, reasonText,
                cancellationToken);
        }
    }

    private async Task<InventoryFutureAllocationResultV1> ReleaseLockedAsync(
        IDbContextTransaction transaction,
        Guid allocationId,
        long expectedVersion,
        FutureSupplyQuantity? quantity,
        string reasonCode,
        string reasonText,
        InventoryCommandMetadata metadata,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        Guid companyId,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var purchaseLineId = await dbContext.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.Id == allocationId)
            .Select(row => (Guid?)row.PurchaseOrderLineId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference);

        // §3.3: a release changes only the purchase line ledger, so it takes only that line's lock (never the Sales line lock).
        await AcquirePurchaseLineAsync(dbContext.Database.GetDbConnection(), transaction.GetDbTransaction(), purchaseLineId,
            cancellationToken);
        await fence.RequireStillAsync(cancellationToken);

        var row = await dbContext.FutureSupplyAllocations.SingleOrDefaultAsync(item => item.Id == allocationId, cancellationToken)
            ?? throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference);
        if (row.PurchaseOrderLineId != purchaseLineId)
        {
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }

        var now = timeProvider.GetUtcNow();
        var step = await Engine.ReleaseTrackedAsync(dbContext, row, expectedVersion, quantity, reasonCode,
            InventoryFutureAllocationOriginsV1.Inventory, reasonText, now, cancellationToken);
        dbContext.Commands.Add(CommandRow(companyId, InventoryFutureAllocationCommandsV1.Release, keyHash, payloadHash, metadata,
            row.Id, step.After, now));
        Engine.Audit(auditEvents, metadata, companyId, row.Id, InventoryFutureAllocationReleasedOutboxV1.EventType,
            Engine.ReleasedOperation, step.VersionBefore, row.Version, JsonSerializer.Serialize(step.Before, Engine.Json),
            JsonSerializer.Serialize(step.After, Engine.Json), reasonText);
        Engine.AddReleasedOutbox(dbContext, row, step, metadata, now);

        return await SaveAndCommitAsync(transaction, fence, InventoryFutureAllocationCommandsV1.Release, keyHash, payloadHash,
            metadata, step.After, cancellationToken);
    }

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Gelecek tedarik ayırması çözümlenmiş şirket bağlamı ister.");
        }

        return companyContext.CompanyId;
    }

    private async Task AcquireSalesLineAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, Guid salesOrderLineId, CancellationToken cancellationToken)
    {
        try
        {
            await salesLineLocks.Bind(connection, transaction).AcquireAsync(salesOrderLineId, cancellationToken);
        }
        catch (SalesOrderLineBusyException exception)
        {
            throw Engine.Error(409, exception.Code, exception);
        }
    }

    private async Task AcquirePurchaseLineAsync(System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction, Guid purchaseOrderLineId, CancellationToken cancellationToken)
    {
        try
        {
            await purchaseLineLocks.Bind(connection, transaction).AcquireAsync(purchaseOrderLineId, cancellationToken);
        }
        catch (PurchaseLineBusyException exception)
        {
            throw Engine.Error(409, exception.Code, exception);
        }
    }

    private async Task<InventoryFutureDemandLineV1> ReadDemandAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken)
    {
        var answer = await demandSource.GetDemandLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
        return answer switch
        {
            { Outcome: InventoryFutureDemandSourceOutcomeV1.Open, Line: { } line }
                when line.OrderId == salesOrderId && line.LineId == salesOrderLineId && line.UnitId != Guid.Empty
                    && line.ProductId != Guid.Empty && !string.IsNullOrEmpty(line.ProductVersion) => line,
            { Outcome: InventoryFutureDemandSourceOutcomeV1.NotFound } =>
                throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference),
            { Outcome: InventoryFutureDemandSourceOutcomeV1.NotApproved } =>
                throw Engine.Error(422, InventoryFutureAllocationCodesV1.InvalidReference),
            _ => throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable),
        };
    }

    private async Task<InventoryFutureSupplyLineV1> ReadSupplyAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken)
    {
        var answer = await supplySource.GetAllocatableLineAsync(purchaseOrderId, purchaseOrderLineId, cancellationToken);
        return answer switch
        {
            { Outcome: InventoryFutureSupplySourceOutcomeV1.Allocatable, Line: { } line }
                when line.OrderId == purchaseOrderId && line.LineId == purchaseOrderLineId && line.UnitId != Guid.Empty
                    && line.ProductId != Guid.Empty && !string.IsNullOrEmpty(line.ProductVersion)
                    && line.ObservedOrderVersion >= 0 => line,
            { Outcome: InventoryFutureSupplySourceOutcomeV1.NotFound } =>
                throw Engine.Error(404, InventoryFutureAllocationCodesV1.InvalidReference),
            { Outcome: InventoryFutureSupplySourceOutcomeV1.NotAllocatable } =>
                throw Engine.Error(422, InventoryFutureAllocationCodesV1.InvalidReference),
            _ => throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable),
        };
    }

    private async Task<FutureSupplyQuantity> ToPurchaseUnitAsync(InventoryFutureSupplyLineV1 supply,
        FutureSupplyQuantity requested, Guid requestUnitId, CancellationToken cancellationToken)
    {
        var converted = await unitConverter.ConvertAsync(supply.ProductId, supply.ProductVersion, requested.Value, requestUnitId,
            supply.UnitId, cancellationToken);
        if (converted.Outcome != InventoryFutureUnitConversionOutcomeV1.Converted || converted.Quantity is not { } value)
        {
            throw Engine.Error(422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable);
        }

        try
        {
            // More decimals than the purchase line keeps is refused, never rounded (no silent digit cutting).
            return FutureSupplyQuantity.Positive(value);
        }
        catch (FutureSupplyRuleException exception)
        {
            throw Engine.Error(422, InventoryFutureAllocationCodesV1.ValidationFailed, exception);
        }
    }

    /// <summary>
    /// The tracked ledger row of the purchase line (or an empty ledger). Fail-closed: a row of another order or unit, a row that
    /// breaks its rules, or a row that disagrees with Σ remaining of the line's active allocations answers 503 and writes nothing.
    /// </summary>
    private async Task<(FutureSupplyLineRow? Row, FutureSupplyLedger Ledger)> LoadLedgerAsync(Guid companyId,
        InventoryFutureSupplyLineV1 supply, CancellationToken cancellationToken)
    {
        var row = await dbContext.FutureSupplyLines
            .SingleOrDefaultAsync(ledger => ledger.PurchaseOrderLineId == supply.LineId, cancellationToken);
        var activeRemaining = await dbContext.FutureSupplyAllocations.AsNoTracking()
            .Where(allocation => allocation.PurchaseOrderLineId == supply.LineId
                && allocation.Status == (int)FutureSupplyAllocationStatus.Active)
            .SumAsync(allocation => (decimal?)allocation.RemainingQuantity, cancellationToken) ?? 0m;
        if (row is null)
        {
            return activeRemaining == 0m
                ? (null, FutureSupplyLedger.Empty(companyId, supply.LineId))
                : throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }

        if (row.PurchaseOrderId != supply.OrderId || row.PurchaseUnitId != supply.UnitId || row.ActiveAllocated != activeRemaining)
        {
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }

        try
        {
            return (row, FutureSupplyLedger.Restore(row.CompanyId, row.PurchaseOrderLineId,
                FutureSupplyQuantity.Of(row.AllocatableSnapshot), FutureSupplyQuantity.Of(row.ActiveAllocated), row.Version));
        }
        catch (Exception exception) when (exception is ArgumentException or FutureSupplyRuleException)
        {
            throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, exception);
        }
    }

    /// <summary>
    /// F-017 STOK §3.2 step 5 / KLN-D65: physical active + future active + this request must not exceed the Sales line's open
    /// quantity. The shared <see cref="InventoryAllocationDemandCap"/> compares exactly in the Sales order unit when nothing needs a
    /// conversion, and in kilograms otherwise (KLN-D66); a missing conversion is a 422, never a guess.
    /// </summary>
    private async Task RequireDemandRoomAsync(InventoryFutureDemandLineV1 demand, FutureSupplyQuantity quantity,
        Guid purchaseUnitId, CancellationToken cancellationToken)
    {
        var outcome = await InventoryAllocationDemandCap.CheckAsync(dbContext, unitConverter, demand,
            InventoryAllocationDemandCapRequest.Future(quantity.Value, purchaseUnitId), 0m, cancellationToken);
        switch (outcome)
        {
            case InventoryAllocationDemandCapOutcome.Fits:
                return;
            case InventoryAllocationDemandCapOutcome.Exceeds:
                throw Engine.Error(409, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity);
            case InventoryAllocationDemandCapOutcome.ConversionUnavailable:
                throw Engine.Error(422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable);
            default:
                throw Engine.Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }
    }

    private async Task<InventoryFutureAllocationResultV1> SaveAndCommitAsync(
        IDbContextTransaction transaction,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        string command,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        InventoryFutureAllocationViewV1 view,
        CancellationToken cancellationToken)
    {
        await fence.RequireStillAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            await transaction.RollbackAsync(CancellationToken.None);
            if (await FindReplayAsync(command, keyHash, payloadHash, metadata.ActorId, cancellationToken) is { } winner)
            {
                return winner;
            }

            throw Engine.Error(409, InventoryFutureAllocationCodesV1.ConcurrentChange, exception);
        }

        await transaction.CommitAsync(cancellationToken);
        return new InventoryFutureAllocationResultV1(view, false, metadata.CorrelationId);
    }

    private async Task<InventoryFutureAllocationResultV1?> FindReplayAsync(string command, string keyHash, string payloadHash,
        string actorId, CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId, StringComparison.Ordinal))
        {
            throw Engine.Error(409, InventoryFutureAllocationCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<CommandResult>(prior.ResultJson, Engine.Json)
            ?? throw new InvalidOperationException("Kayıtlı gelecek tedarik ayırma komut sonucu okunamadı.");
        if (stored.Allocation is { } allocation)
        {
            return new InventoryFutureAllocationResultV1(allocation, true, prior.CorrelationId);
        }

        // A recorded terminal refusal answers the same key with the same refusal (and the first attempt's correlation).
        if (stored.RefusalStatus is { } status && !string.IsNullOrEmpty(stored.RefusalCode))
        {
            throw new InventoryAllocationException(status, stored.RefusalCode, isReplay: true,
                originalCorrelationId: prior.CorrelationId);
        }

        throw new InvalidOperationException("Kayıtlı gelecek tedarik ayırma komut sonucu okunamadı.");
    }

    /// <summary>
    /// Records a terminal refusal decided under the locks: the tracked changes are dropped, the command row keeps status + code
    /// (no allocation) and one audit event names the refusal; nothing else is written. A lost race on the key answers the winner.
    /// </summary>
    private async Task<InventoryFutureAllocationResultV1> RecordRefusalAsync(
        IDbContextTransaction transaction,
        IInventoryFutureAllocationAuthorityFenceV1 fence,
        string command,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid companyId,
        Guid targetId,
        InventoryAllocationException refusal,
        string eventType,
        string operation,
        string reason,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var now = timeProvider.GetUtcNow();
        dbContext.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new CommandResult(null, refusal.Status, refusal.Code), Engine.Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = now,
        });
        Engine.Audit(auditEvents, metadata, companyId, targetId, eventType, operation, null, null, "{}",
            JsonSerializer.Serialize(new { status = refusal.Status, code = refusal.Code }, Engine.Json), reason);
        await fence.RequireStillAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            await transaction.RollbackAsync(CancellationToken.None);
            if (await FindReplayAsync(command, keyHash, payloadHash, metadata.ActorId, cancellationToken) is { } winner)
            {
                return winner;
            }

            throw Engine.Error(409, InventoryFutureAllocationCodesV1.ConcurrentChange, exception);
        }

        await transaction.CommitAsync(cancellationToken);
        throw refusal;
    }

    private static InventoryCommandRow CommandRow(Guid companyId, string command, string keyHash, string payloadHash,
        InventoryCommandMetadata metadata, Guid targetId, InventoryFutureAllocationViewV1 view, DateTimeOffset now) => new()
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new CommandResult(view, null, null), Engine.Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = now,
        };

    internal const string CreateRefusedEventType = "inventory.future-allocation.create-refused.v1";
    internal const string ReleaseRefusedEventType = "inventory.future-allocation.release-refused.v1";
    private const string CreateRefusedOperation = "CreateRefused";
    private const string ReleaseRefusedOperation = "ReleaseRefused";

    /// <summary>Create refusals decided under the locks that the same input can never pass (recorded, replayed by the key).</summary>
    internal static readonly IReadOnlySet<(int Status, string Code)> RecordableCreateRefusals = new HashSet<(int, string)>
    {
        (409, InventoryFutureAllocationCodesV1.StateConflict),
        (409, InventoryFutureAllocationCodesV1.InsufficientFutureSupply),
        (409, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity),
        (422, InventoryFutureAllocationCodesV1.InvalidReference),
        (422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable),
        (422, InventoryFutureAllocationCodesV1.ValidationFailed),
    };

    /// <summary>Release refusals decided under the purchase line lock (stale version, closed allocation, invalid selection).</summary>
    internal static readonly IReadOnlySet<(int Status, string Code)> RecordableReleaseRefusals = new HashSet<(int, string)>
    {
        (409, InventoryFutureAllocationCodesV1.StateConflict),
        (409, InventoryFutureAllocationCodesV1.VersionConflict),
        (422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid),
    };

    /// <summary>The stored answer of a key: the allocation, or a terminal refusal's status and code (never both).</summary>
    private sealed record CommandResult(InventoryFutureAllocationViewV1? Allocation, int? RefusalStatus, string? RefusalCode);
}
