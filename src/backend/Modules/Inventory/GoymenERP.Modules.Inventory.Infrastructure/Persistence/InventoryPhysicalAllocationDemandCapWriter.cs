using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Engine = GoymenERP.Modules.Inventory.Infrastructure.Persistence.InventoryFutureAllocationEngine;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D65 (F-017 STOK spec §9.1 candidate 3) on the physical allocation path: a new physical reservation (a create, or the
/// growing part of a change) must keep physical + active future allocations within the Sales line's open quantity, the same
/// <see cref="InventoryAllocationDemandCap"/> the future supply allocation writer uses. Both paths take the SAME Sales order line
/// lock first (<see cref="ISalesOrderLineSerializationLockBinderV1"/>) in Inventory's open transaction, so a physical create and
/// a future create on one line can never both pass the cap (KLN-D65 (3)).
/// <para>
/// The decorated <see cref="InventoryStore"/> keeps every decided rule (label version, free stock, ownership gate, one save of
/// Inventory's own rows); this class only opens the transaction, takes the lock, reads the open demand and refuses before the
/// store runs. An exceeding request is 409 <see cref="InventoryAllocationCodesV1.ExceedsDemandOpenQuantity"/> and existing records
/// stay as they are (KLN-D65 (2)); a kilogram comparison without a conversion source is 422
/// <see cref="InventoryAllocationCodesV1.UnitConversionUnavailable"/> (KLN-D66 (3)). A replayed key and a malformed request go
/// straight to the store (its replay and 400 answers stay first). A release only lowers the total and is not capped.
/// </para>
/// <para>
/// A change that does not grow the allocation (same or fewer kilograms) is never refused by the cap: it can only lower the
/// total, so a line already above its cap (for example after a Sales quantity decrease) can still be brought down.
/// </para>
/// </summary>
public sealed class InventoryPhysicalAllocationDemandCapWriter(
    InventoryStore inner,
    InventoryDbContext dbContext,
    IInventoryFutureDemandSourceV1 demandSource,
    IInventoryFutureUnitConverterV1 unitConverter,
    ISalesOrderLineSerializationLockBinderV1? salesLineLocks) : IInventoryPhysicalAllocationWriterV1
{
    public async Task<InventoryPhysicalAllocationResultV1> CreateAsync(
        CreatePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (request is null || metadata is null || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty || request.ProductId == Guid.Empty
            || !TryKilograms(request.QuantityKg, out var kilograms)
            || await IsReplayAsync(InventoryStore.CreatePhysicalAllocationCommand, metadata.IdempotencyKey, cancellationToken))
        {
            // The store answers the shape (400) and the replay (the stored result or 409 idempotency_conflict) itself.
            return await inner.CreateAsync(request!, metadata!, cancellationToken);
        }

        await using var transaction = await BeginLockedAsync(request.SalesOrderLineId, cancellationToken);
        await RequireRoomAsync(request.SalesOrderId, request.SalesOrderLineId, request.ProductId,
            InventoryAllocationDemandCapRequest.Physical(kilograms), 0m, cancellationToken);
        var result = await inner.CreateAsync(request, metadata, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default) =>
        inner.ReleaseAsync(allocationId, request, metadata, cancellationToken);

    public async Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
        Guid allocationId,
        ChangePhysicalSalesAllocationV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (allocationId == Guid.Empty || request is null || metadata is null
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty || request.ProductId == Guid.Empty
            || !TryKilograms(request.QuantityKg, out var kilograms)
            || await IsReplayAsync(InventoryStore.ChangePhysicalAllocationCommand, metadata.IdempotencyKey, cancellationToken))
        {
            return await inner.ChangeAsync(allocationId, request!, metadata!, cancellationToken);
        }

        await using var transaction = await BeginLockedAsync(request.SalesOrderLineId, cancellationToken);
        // Read under the lock: a concurrent change of the same allocation on this line waits for this transaction.
        var current = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.Id == allocationId)
            .Select(row => new { row.DemandKind, row.DemandId, row.DemandLineId, row.RemainingKg })
            .SingleOrDefaultAsync(cancellationToken);
        var servesLine = current is not null
            && string.Equals(current.DemandKind, InventoryAllocationDemandKindsV1.SalesOrderLine, StringComparison.Ordinal)
            && current.DemandId == request.SalesOrderId && current.DemandLineId == request.SalesOrderLineId;
        if (servesLine && kilograms > current!.RemainingKg)
        {
            // Resize in place and move both replace the allocation's remaining kilograms with the request.
            await RequireRoomAsync(request.SalesOrderId, request.SalesOrderLineId, request.ProductId,
                InventoryAllocationDemandCapRequest.Physical(kilograms), current.RemainingKg, cancellationToken);
        }

        // A missing allocation (404), another demand (422) and a state or version refusal stay the store's own answers.
        var result = await inner.ChangeAsync(allocationId, request, metadata, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<IDbContextTransaction> BeginLockedAsync(Guid salesOrderLineId, CancellationToken cancellationToken)
    {
        if (salesLineLocks is null)
        {
            // Without the Sales line lock the two paths cannot be serialised; fail-closed instead of an unguarded cap.
            throw Engine.Error(503, InventoryAllocationCodesV1.InvalidReference);
        }

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await salesLineLocks.Bind(dbContext.Database.GetDbConnection(), transaction.GetDbTransaction())
                .AcquireAsync(salesOrderLineId, cancellationToken);
            return transaction;
        }
        catch (SalesOrderLineBusyException exception)
        {
            await transaction.DisposeAsync();
            throw Engine.Error(409, exception.Code, exception);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task RequireRoomAsync(Guid salesOrderId, Guid salesOrderLineId, Guid productId,
        InventoryAllocationDemandCapRequest request, decimal replacedKilograms, CancellationToken cancellationToken)
    {
        var answer = await demandSource.GetDemandLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
        var demand = answer switch
        {
            { Outcome: InventoryFutureDemandSourceOutcomeV1.Open, Line: { } line }
                when line.OrderId == salesOrderId && line.LineId == salesOrderLineId && line.UnitId != Guid.Empty
                    && line.ProductId != Guid.Empty => line,
            { Outcome: InventoryFutureDemandSourceOutcomeV1.NotFound } =>
                throw Engine.Error(404, InventoryAllocationCodesV1.InvalidReference),
            { Outcome: InventoryFutureDemandSourceOutcomeV1.NotApproved } =>
                throw Engine.Error(422, InventoryAllocationCodesV1.InvalidReference),
            _ => throw Engine.Error(503, InventoryAllocationCodesV1.InvalidReference),
        };

        // KLN-D67: the product identity decides (the card revision may differ between the line and the label).
        if (demand.ProductId != productId)
        {
            throw Engine.Error(422, InventoryAllocationCodesV1.InvalidReference);
        }

        var outcome = await InventoryAllocationDemandCap.CheckAsync(dbContext, unitConverter, demand, request,
            replacedKilograms, cancellationToken);
        switch (outcome)
        {
            case InventoryAllocationDemandCapOutcome.Fits:
                return;
            case InventoryAllocationDemandCapOutcome.Exceeds:
                throw Engine.Error(409, InventoryAllocationCodesV1.ExceedsDemandOpenQuantity);
            case InventoryAllocationDemandCapOutcome.ConversionUnavailable:
                throw Engine.Error(422, InventoryAllocationCodesV1.UnitConversionUnavailable);
            default:
                throw Engine.Error(503, InventoryAllocationCodesV1.InvalidReference);
        }
    }

    private Task<bool> IsReplayAsync(string command, string idempotencyKey, CancellationToken cancellationToken)
    {
        var keyHash = Engine.KeyHash(idempotencyKey);
        return dbContext.Commands.AsNoTracking()
            .AnyAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
    }

    /// <summary>
    /// The store's own kilogram shape (ASCII digits, one dot, at most three decimals, positive); anything else goes to the
    /// store untouched so its 400 stays the answer. The store remains the authority on the value it writes.
    /// </summary>
    private static bool TryKilograms(string? text, out decimal kilograms)
    {
        kilograms = 0m;
        return !string.IsNullOrEmpty(text) && text.Length <= MaximumQuantityTextLength
            && text.Count(character => character == '.') <= 1
            && text.All(character => char.IsAsciiDigit(character) || character == '.')
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out kilograms)
            && kilograms > 0m && kilograms.Scale <= MaximumKilogramDecimals;
    }

    // The store's TryQuantity limits (InventoryStore.Allocations), repeated only to keep its 400 ahead of the cap.
    private const int MaximumQuantityTextLength = 40;
    private const int MaximumKilogramDecimals = 3;
}
