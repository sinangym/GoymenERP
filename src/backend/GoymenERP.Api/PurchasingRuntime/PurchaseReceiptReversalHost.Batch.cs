using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.PurchasingRuntime;

internal sealed partial class PurchaseReceiptReversalHost
{
    public async Task<PurchaseReceiptReversalBatchResponseV1> ExecuteBatchAsync(Guid companyId,
        PurchaseReceiptReversalBatchWriteV1 write, PurchasingWriteEnvelope envelope,
        Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        if (batchOwnerFactory is null || companyId == Guid.Empty || write.OrderId == Guid.Empty) throw Unavailable();
        PurchaseReceiptReversalBatchJsonV1.Validate(write.Request);
        var hash = PurchaseReceiptReversalBatchHashV1.Compute(write);
        const string command = PurchaseReceiptReversalBatchApiV1.CommandName;
        try
        {
            for (var attempt = 0; attempt < PurchaseGoodsReceiptPrelock.MaximumPreparationAttempts; attempt++)
            {
                await freshAuthority(cancellationToken);
                await using var transaction = await purchasing.Database.BeginTransactionAsync(cancellationToken);
                var connection = purchasing.Database.GetDbConnection(); var borrowed = transaction.GetDbTransaction();
                await using var purchase = await batchOwnerFactory(connection, borrowed, companyId, cancellationToken);
                var supply = new BatchPostAdjustmentSupply(purchase);
                await using var inventory = await EnlistInventoryAsync(connection, borrowed, supply);
                try
                {
                    // Immutable replay is intentionally before current target/version/allocation reads.
                    var prior = await Checked(() => purchase.FindBatchReplayAsync(command, hash, envelope, cancellationToken));
                    if (prior is not null)
                    {
                        RequireBatchResult(prior, companyId, write);
                        var response = await CompleteAsync(prior, null);
                        await freshAuthority(cancellationToken); await transaction.CommitAsync(cancellationToken);
                        return response;
                    }
                    if (!await PrepareBatchAsync(companyId, write, purchase, inventory, connection, borrowed, freshAuthority, cancellationToken))
                    { await RollbackQuietlyAsync(transaction); purchasing.ChangeTracker.Clear(); continue; }
                    var targets = RequireBatchTargets(await Checked(() => purchase.ReadTargetsAsync(write.OrderId,
                        write.Request.Lines.Select(line => line.AcceptanceId).ToArray(), cancellationToken)), companyId, write);
                    supply.Targets = targets.Values.GroupBy(target => target.GeneralLineId).ToDictionary(group => group.Key, group =>
                    {
                        var first = group.First();
                        if (group.Any(target => target.ProductId != first.ProductId || target.ProductVersion != first.ProductVersion || target.UnitId != first.UnitId))
                            throw Unavailable();
                        return first;
                    });
                    var correctionRefusal = await PreflightCorrectionsAsync();
                    var adjusted = await Checked(() => purchase.StageBatchAsync(new(companyId, write.Request.BatchId, write.OrderId,
                        write.Request.ExpectedPurchaseOrderVersion, write.Request.Reason, write.Request.OccurredAtUtc, command, hash,
                        write.Request.Lines.Select(line => new PurchaseReceiptAdjustmentBatchLineV1(line.AdjustmentId, line.AcceptanceId,
                            line.UndoQuantity, false, false, line.ReplacementReceipt?.AcceptanceId, line.ReplacementReceipt?.AcceptedQuantity)).ToArray()),
                        envelope, cancellationToken));
                    RequireBatchResult(adjusted, companyId, write);
                    var supplies = adjusted.Supplies.ToDictionary(value => value.GeneralLineId);
                    if (!supplies.Keys.ToHashSet().SetEquals(targets.Values.Select(target => target.GeneralLineId))) throw Unavailable();
                    foreach (var fact in supplies.Values)
                    {
                        var actual = await Checked(() => purchase.ReadSupplyAsync(write.OrderId, fact.GeneralLineId, cancellationToken)) ?? throw Unavailable();
                        if (actual.CompanyId != companyId || actual.OrderId != write.OrderId || actual.GeneralLineId != fact.GeneralLineId
                            || actual.OrderVersion != adjusted.OrderVersion || actual.AllocatableQuantity != fact.AllocatableQuantity
                            || fact.NetOpenedQuantity != "0" && !actual.IsAllocatable) throw Unavailable();
                    }
                    var evaluation = write.Request.Lines.Select(line =>
                    {
                        var target = targets[line.AcceptanceId]; var fact = supplies[target.GeneralLineId];
                        // Reserve the whole general-line replacement budget. The successive Inventory stages consume it cumulatively.
                        var replacement = write.Request.Lines.Where(candidate => targets[candidate.AcceptanceId].GeneralLineId == target.GeneralLineId)
                            .Sum(candidate => QuantityText(candidate.ReplacementReceipt?.AcceptedQuantity ?? "0"));
                        return new InventoryGoodsReceiptReversalBatchLineV1(StockRequest(line), new(companyId, write.OrderId,
                            target.GeneralLineId, Version(adjusted.OrderVersion), fact.NetOpenedQuantity, fact.AllocatableQuantity,
                            replacement.ToString("0.###", CultureInfo.InvariantCulture)), ChildMetadata(line));
                    }).ToArray();
                    if (inventory is not IInventoryGoodsReceiptReversalBatchParticipantV1 batchInventory) throw Unavailable();
                    try { await batchInventory.EvaluateBatchAsync(evaluation, cancellationToken); }
                    catch (InventoryGoodsReceiptReversalException exception) when (exception.Status is 409 or 422 && correctionRefusal is not null)
                    { throw correctionRefusal; }
                    await freshAuthority(cancellationToken);
                    if (correctionRefusal is not null) throw correctionRefusal;
                    var responseWritten = await CompleteAsync(adjusted, (targets, evaluation));
                    await freshAuthority(cancellationToken); await transaction.CommitAsync(cancellationToken);
                    return responseWritten;

                    async Task<PurchaseReceiptReversalBatchResponseV1> CompleteAsync(PurchaseReceiptAdjustmentBatchResultV1 result,
                        (Dictionary<Guid, PurchaseReceiptAdjustmentTargetV1> Targets, InventoryGoodsReceiptReversalBatchLineV1[] Evaluation)? fresh)
                    {
                        var reversed = new Dictionary<Guid, InventoryGoodsReceiptReversalResultV1>();
                        foreach (var line in write.Request.Lines)
                        {
                            InventoryGoodsReceiptReversalResultV1 answer;
                            if (fresh is null)
                                answer = await Checked(() => inventory.ReadReplayAsync(StockRequest(line), ChildMetadata(line), cancellationToken)) ?? throw Unavailable();
                            else
                            {
                                // A participant stages once. Every participant borrows the exact same owner transaction.
                                await using var stage = await EnlistInventoryAsync(connection, borrowed, supply);
                                var item = fresh.Value.Evaluation.Single(item => item.Request.ReversalId == line.AdjustmentId);
                                answer = await Checked(() => stage.StageAsync(item.Request, item.ExpectedSupply, item.Metadata, cancellationToken));
                            }
                            if (answer.CompanyId != companyId || answer.ReversalId != line.AdjustmentId || answer.AcceptanceId != line.AcceptanceId
                                || answer.InventoryMovementId != line.InventoryMovementId || answer.QuantityKg != line.MeasuredUndoQuantityKg
                                || answer.IsReplay != result.IsReplay || answer.OriginalCorrelationId != result.OriginalCorrelationId) throw Unavailable();
                            reversed.Add(line.AcceptanceId, answer);
                        }
                        // All reverse movements precede every replacement receipt/conversion. Reconsume the complete group's reopened IDs.
                        var replies = new List<PurchaseReceiptReversalResponseV1>();
                        foreach (var line in write.Request.Lines)
                        {
                            var adjustedLine = result.Lines.Single(value => value.AcceptanceId == line.AcceptanceId);
                            var reopened = result.Lines.Where(value => value.GeneralLineId == adjustedLine.GeneralLineId)
                                .SelectMany(value => reversed[value.AcceptanceId].Conversions).Where(value => value.ReopenedQuantity != "0")
                                .Select(value => value.FutureAllocationId).Distinct().ToArray();
                            var correction = await CorrectionAsync(connection, borrowed, null, companyId, Single(line), adjustedLine,
                                reversed[line.AcceptanceId], envelope, freshAuthority, cancellationToken,
                                fresh?.Targets[line.AcceptanceId], reopened, ChildMetadata(line));
                            replies.Add(new(adjustedLine, View(reversed[line.AcceptanceId]), correction));
                        }
                        return new(companyId, write.OrderId, result.BatchId, result.OrderVersion, replies, result.Supplies,
                            result.Supplies.Any(value => value.Revived), result.IsReplay, result.OriginalCorrelationId);
                    }
                    async Task<PurchasingServiceException?> PreflightCorrectionsAsync()
                    {
                        PurchasingServiceException? refusal = null;
                        foreach (var line in write.Request.Lines)
                        {
                            if (line.ReplacementReceipt is not { } next) continue;
                            var target = targets[line.AcceptanceId];
                            if (next.ExpectedProductVersion != target.ProductVersion)
                                refusal ??= new(409, PurchaseAcceptanceParticipantCodes.ProductVersionConflict);
                            var location = await Checked(() => locations.ReadAsync(companyId, next.LocationId, cancellationToken));
                            if (location is null || !location.IsActive || location.IsExternal)
                                refusal ??= new(404, InventoryGoodsReceiptCodesV1.InvalidReference);
                            var scale = await Checked(() => new InventoryKilogramScale(services.GetRequiredService<ISettingsPrerequisiteProvider>())
                                .CheckAsync(companyId, DateOnly.FromDateTime(next.OccurredAtUtc.UtcDateTime),
                                    [Kilograms.Positive(QuantityText(next.MeasuredQuantityKg))], cancellationToken));
                            if (!scale.IsAccepted) throw new PurchasingServiceException(503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable);
                        }
                        return refusal;
                    }
                }
                catch { await RollbackQuietlyAsync(transaction); purchasing.ChangeTracker.Clear(); throw; }
            }
            throw PurchaseGoodsReceiptPrelock.RetryExhausted();
        }
        catch (InventoryGoodsReceiptReversalException exception) { throw new PurchasingServiceException(exception.Status, exception.Code); }
        catch (InventoryGoodsReceiptException exception) { throw new PurchasingServiceException(exception.Status, exception.Code); }
        catch (InventoryAllocationException exception)
        { throw exception.Status == 409 ? new PurchasingServiceException(409, InventoryGoodsReceiptCodesV1.ConcurrentChange) : new PurchasingServiceException(503, InventoryGoodsReceiptCodesV1.Unavailable); }
        catch (SalesOrderLineBusyException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (PurchaseLineBusyException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (DbUpdateConcurrencyException) { throw PurchaseGoodsReceiptPrelock.RetryExhausted(); }

        async Task<IInventoryGoodsReceiptReversalParticipantV1> EnlistInventoryAsync(DbConnection connection, DbTransaction transaction,
            IInventoryFutureSupplySourceV1 sameTransactionSupply) => await InventoryGoodsReceiptReversalParticipant.EnlistAsync(connection, transaction,
                companyId, services.GetRequiredService<ISettingsPrerequisiteProvider>(), services.GetRequiredService<IInventoryFutureDemandSourceV1>(),
                services.GetRequiredService<IInventoryFutureUnitConverterV1>(), sameTransactionSupply,
                services.GetRequiredService<IAuditEventCollector>(), services.GetRequiredService<IAuditTransactionWriterV1>(), cancellationToken, clock);
        async Task<T> Checked<T>(Func<Task<T>> source)
        { var value = await source(); await freshAuthority(cancellationToken); return value; }
        PurchaseReceiptReversalWriteV1 Single(ReversePurchaseReceiptBatchLineV1 line) => new(write.OrderId, line.AcceptanceId,
            line.SingleRequest(write.Request), CommandName: command);
        InventoryGoodsReceiptReversalRequestV1 StockRequest(ReversePurchaseReceiptBatchLineV1 line) => new(companyId, line.AdjustmentId,
            line.ReversalLineId, line.AcceptanceId, line.InventoryMovementId, InventoryGoodsReceiptReversalKindV1.AcceptanceUndo,
            line.SourceBlock == "Free" ? BlockState.Free : BlockState.Blocked, line.MeasuredUndoQuantityKg, null, write.Request.Reason, write.Request.OccurredAtUtc);
        InventoryCommandMetadata ChildMetadata(ReversePurchaseReceiptBatchLineV1 line) => Metadata(envelope with
            { IdempotencyKey = BatchChildKey(envelope.IdempotencyKey, line.AdjustmentId) }, command);
    }

    internal static string BatchChildKey(string key, Guid adjustmentId) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(PurchaseReceiptReversalBatchApiV1.CommandName + "\n" + key + "\n" + adjustmentId.ToString("D")))).ToLowerInvariant();
    private static decimal QuantityText(string text) => InventoryFutureAllocationTextV1.TryNonNegativeQuantity(text, out var quantity)
        ? quantity.Value : throw Unavailable();
    private static Dictionary<Guid, PurchaseReceiptAdjustmentTargetV1> RequireBatchTargets(IReadOnlyList<PurchaseReceiptAdjustmentTargetV1>? values,
        Guid company, PurchaseReceiptReversalBatchWriteV1 write)
    {
        if (values is null || values.Any(value => value is null || !write.Request.Lines.Any(line => line.AcceptanceId == value.AcceptanceId))
            || values.Select(value => value.AcceptanceId).Distinct().Count() != values.Count
            || values.Count > 0 && values.Any(value => value.ObservedOrderVersion != values[0].ObservedOrderVersion)) throw Unavailable();
        if (values.Count != write.Request.Lines.Count) throw new PurchasingServiceException(404, "not_found");
        return write.Request.Lines.ToDictionary(line => line.AcceptanceId, line => RequireTarget(values.SingleOrDefault(value => value.AcceptanceId == line.AcceptanceId),
            company, new(write.OrderId, line.AcceptanceId, line.SingleRequest(write.Request))));
    }
    private static void RequireBatchResult(PurchaseReceiptAdjustmentBatchResultV1 result, Guid company, PurchaseReceiptReversalBatchWriteV1 write)
    {
        if (result is null || result.CompanyId != company || result.OrderId != write.OrderId || result.BatchId != write.Request.BatchId
            || !CanonicalVersion(result.OrderVersion) || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || result.Lines is null || result.Supplies is null
            || result.Lines.Count != write.Request.Lines.Count || result.Lines.Any(value => value is null || value.OrderVersion != result.OrderVersion
                || value.IsReplay != result.IsReplay || value.OriginalCorrelationId != result.OriginalCorrelationId)
            || result.Lines.Select(value => value.AcceptanceId).Distinct().Count() != result.Lines.Count
            || result.Supplies.Any(value => value is null || value.GeneralLineId == Guid.Empty
                || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(value.NetOpenedQuantity, out _)
                || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(value.AllocatableQuantity, out _))
            || result.Supplies.Select(value => value.GeneralLineId).Distinct().Count() != result.Supplies.Count
            || !result.Supplies.Select(value => value.GeneralLineId).ToHashSet().SetEquals(result.Lines.Select(value => value.GeneralLineId))) throw Unavailable();
        foreach (var line in write.Request.Lines) RequireResult(result.Lines.SingleOrDefault(value => value.AcceptanceId == line.AcceptanceId)!,
            company, new(write.OrderId, line.AcceptanceId, line.SingleRequest(write.Request)));
    }
    private sealed class BatchPostAdjustmentSupply(IPurchaseReceiptAdjustmentBatchParticipantV1 owner) : IInventoryFutureSupplySourceV1
    {
        public IReadOnlyDictionary<Guid, PurchaseReceiptAdjustmentTargetV1> Targets { get; set; } = new Dictionary<Guid, PurchaseReceiptAdjustmentTargetV1>();
        public async Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
        {
            if (!Targets.TryGetValue(lineId, out var target) || target.OrderId != orderId) throw Unavailable();
            var value = await owner.ReadSupplyAsync(orderId, lineId, cancellationToken) ?? throw Unavailable();
            if (value.CompanyId != target.CompanyId || value.OrderId != orderId || value.GeneralLineId != lineId) throw Unavailable();
            return new(value.IsAllocatable ? InventoryFutureSupplySourceOutcomeV1.Allocatable : InventoryFutureSupplySourceOutcomeV1.NotAllocatable,
                new(orderId, lineId, target.ProductId, target.ProductVersion, target.UnitId, value.AllocatableQuantity, ObservedOrderVersion: Version(value.OrderVersion)));
        }
    }
}
