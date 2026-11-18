using GoymenERP.Api.InventoryRuntime;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>API-44 / F017-e: owners borrow one transaction. All retries end before the first owner write.</summary>
internal sealed partial class PurchaseReceiptReversalHost(PurchasingDbContext purchasing, IServiceProvider services,
    IOrganizationLocationStatusReaderV1 locations, TimeProvider clock,
    PurchaseReceiptAdjustmentParticipantFactoryV1? ownerFactory = null,
    PurchaseReceiptAdjustmentBatchParticipantFactoryV1? batchOwnerFactory = null) : IPurchaseReceiptReversalTransactionV1,
    IPurchaseReceiptReversalBatchTransactionV1
{
    public Task<PurchaseReceiptReversalResponseV1> ExecuteAsync(Guid companyId, PurchaseReceiptReversalWriteV1 write,
        PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority,
        CancellationToken cancellationToken = default) => ExecuteWithWriteAsync(companyId, write, envelope,
            (response, _) => Task.FromResult(response), freshAuthority, cancellationToken);

    public async Task<T> ExecuteWithWriteAsync<T>(Guid companyId, PurchaseReceiptReversalWriteV1 write,
        PurchasingWriteEnvelope envelope, Func<PurchaseReceiptReversalResponseV1, CancellationToken, Task<T>> writeReturn,
        Func<CancellationToken, Task> freshAuthority,
        CancellationToken cancellationToken = default)
    {
        if (ownerFactory is null) throw Unavailable();
        if (companyId == Guid.Empty || write.OrderId == Guid.Empty || write.AcceptanceId == Guid.Empty) throw Unavailable();
        PurchaseReceiptReversalJsonV1.Validate(write.Request);
        if (write.Request.ReplacementReceipt?.AcceptanceId == write.AcceptanceId
            || write.IsSupplierReturn && write.Request.ReplacementReceipt is not null)
            throw new PurchasingServiceException(400, "validation_failed");
        if (write.CommandName is not (PurchaseReceiptReversalApiV1.CommandName or PurchaseSupplierReturnCommandsV1.CreateV1
            or PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1)
            || !write.IsSupplierReturn && write.CommandName != PurchaseReceiptReversalApiV1.CommandName)
            throw Unavailable();
        var requestHash = write.RequestHash ?? PurchaseReceiptReversalHashV1.Compute(write);
        var metadata = Metadata(envelope, write.CommandName);
        var stockRequest = new InventoryGoodsReceiptReversalRequestV1(companyId, write.Request.AdjustmentId,
            write.Request.ReversalLineId, write.AcceptanceId, write.Request.InventoryMovementId,
            write.IsSupplierReturn ? InventoryGoodsReceiptReversalKindV1.SupplierReturn : InventoryGoodsReceiptReversalKindV1.AcceptanceUndo,
            write.Request.SourceBlock == "Free" ? BlockState.Free : BlockState.Blocked, write.Request.MeasuredUndoQuantityKg,
            write.OriginalUnitCostTryPerKg, write.Request.Reason, write.Request.OccurredAtUtc);
        try
        {
            for (var attempt = 0; attempt < PurchaseGoodsReceiptPrelock.MaximumPreparationAttempts; attempt++)
            {
                await freshAuthority(cancellationToken);
                await using var transaction = await purchasing.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var connection = purchasing.Database.GetDbConnection();
                var borrowed = transaction.GetDbTransaction();
                await using var purchase = await ownerFactory(connection, borrowed, companyId, cancellationToken);
                var supplySource = new PostAdjustmentSupply(purchase);
                await using var inventory = await InventoryGoodsReceiptReversalParticipant.EnlistAsync(connection, borrowed,
                    companyId, services.GetRequiredService<ISettingsPrerequisiteProvider>(),
                    services.GetRequiredService<IInventoryFutureDemandSourceV1>(),
                    services.GetRequiredService<IInventoryFutureUnitConverterV1>(),
                    supplySource, services.GetRequiredService<IAuditEventCollector>(),
                    services.GetRequiredService<IAuditTransactionWriterV1>(), cancellationToken, clock);
                try
                {
                    var replay = await Checked(() => purchase.FindReplayAsync(write.CommandName,
                        requestHash, envelope, cancellationToken));
                    if (replay is not null)
                    {
                        RequireResult(replay, companyId, write);
                        var originalStock = await Checked(() => inventory.ReadReplayAsync(stockRequest, metadata, cancellationToken))
                            ?? throw Unavailable();
                        var corrected = await CorrectionAsync(connection, borrowed, purchase, companyId, write, replay, originalStock,
                            envelope, freshAuthority, cancellationToken);
                        var replayWritten = await writeReturn(new PurchaseReceiptReversalResponseV1(replay, View(originalStock), corrected), cancellationToken);
                        await freshAuthority(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return replayWritten;
                    }

                    var ready = await PrepareAsync(companyId, write, purchase, inventory, connection, borrowed,
                        freshAuthority, cancellationToken);
                    if (!ready)
                    {
                        await RollbackQuietlyAsync(transaction);
                        purchasing.ChangeTracker.Clear();
                        continue;
                    }
                    var target = RequireTarget(await Checked(() => purchase.ReadTargetAsync(write.OrderId, write.AcceptanceId,
                        cancellationToken)), companyId, write);
                    supplySource.Target = target;
                    var next = write.Request.ReplacementReceipt;
                    if (next is not null && next.ExpectedProductVersion != target.ProductVersion)
                        throw new PurchasingServiceException(409, PurchaseAcceptanceParticipantCodes.ProductVersionConflict);
                    var notificationSnapshots = await CaptureRevivalNotificationsAsync(connection, borrowed, companyId, write.OrderId,
                        [target], freshAuthority, cancellationToken);
                    var adjusted = await Checked(() => purchase.StageAsync(new PurchaseReceiptAdjustmentStageV1(companyId, write.Request.AdjustmentId,
                        write.OrderId, write.AcceptanceId, write.Request.ExpectedPurchaseOrderVersion, write.Request.UndoQuantity,
                        write.ReplacementExpected, write.IsSupplierReturn, write.Request.Reason, write.Request.OccurredAtUtc,
                        write.CommandName, requestHash, next?.AcceptanceId, next?.AcceptedQuantity, notificationSnapshots), envelope,
                        cancellationToken));
                    RequireResult(adjusted, companyId, write);
                    var actual = await Checked(() => purchase.ReadSupplyAsync(write.OrderId, target.GeneralLineId, cancellationToken))
                        ?? throw Unavailable();
                    RequireSupply(actual, adjusted, companyId, target);
                    var reversed = await Checked(() => inventory.StageAsync(stockRequest, new InventoryGoodsReceiptReversalSupplyV1(companyId, write.OrderId,
                        target.GeneralLineId, Version(adjusted.OrderVersion), adjusted.NetOpenedQuantity, adjusted.AllocatableQuantity,
                        adjusted.NewAcceptedQuantity), metadata, cancellationToken));
                    var correction = await CorrectionAsync(connection, borrowed, purchase, companyId, write, adjusted, reversed,
                        envelope, freshAuthority, cancellationToken);
                    var written = await writeReturn(new PurchaseReceiptReversalResponseV1(adjusted, View(reversed), correction), cancellationToken);
                    await freshAuthority(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return written;
                }
                catch (Exception exception)
                {
                    var rolledBack = await RollbackQuietlyAsync(transaction);
                    purchasing.ChangeTracker.Clear();
                    await RecordPhysicalRefusalAsync(exception, rolledBack);
                    throw;
                }
            }
            throw PurchaseGoodsReceiptPrelock.RetryExhausted();
        }
        catch (InventoryGoodsReceiptReversalException exception) { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception,
                detail: InventoryPhysicalStockRefusalProblem.Apply(exception.Status, exception, new Dictionary<string, object?>())); }
        catch (InventoryGoodsReceiptException exception) { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception,
                detail: InventoryPhysicalStockRefusalProblem.Apply(exception.Status, exception, new Dictionary<string, object?>())); }
        catch (InventorySupplierReturnExitException exception) { throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception,
                detail: InventoryPhysicalStockRefusalProblem.Apply(exception.Status, exception, new Dictionary<string, object?>())); }
        catch (InventoryAllocationException exception)
        {
            throw exception.Status == 409
                ? new PurchasingServiceException(409, exception.Code == InventoryFutureAllocationCodesV1.IdempotencyConflict
                    ? InventoryGoodsReceiptCodesV1.IdempotencyConflict : InventoryGoodsReceiptCodesV1.ConcurrentChange)
                : new PurchasingServiceException(503, InventoryGoodsReceiptCodesV1.Unavailable);
        }
        catch (SalesOrderLineBusyException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (PurchaseLineBusyException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (DbUpdateConcurrencyException) { throw PurchaseGoodsReceiptPrelock.RetryExhausted(); }

        async Task<TOwner> Checked<TOwner>(Func<Task<TOwner>> read)
        {
            var result = await read();
            await freshAuthority(cancellationToken);
            return result;
        }
    }

    private static PurchaseReceiptReversalStockV1 View(InventoryGoodsReceiptReversalResultV1 result) =>
        new PurchaseReceiptReversalStockV1(result.CompanyId, result.ReversalId, result.AcceptanceId, result.InventoryMovementId, result.OriginalReceiptMovementId,
            result.LabelId, result.LabelVersion, result.QuantityKg, result.Conversions.Select(item => new PurchaseReceiptReversalConversionV1(
                item.ConversionId, item.FutureAllocationId, item.UndoneQuantity, item.UndoneKg, item.ReopenedQuantity,
                item.OverflowReleasedQuantity, item.UnheldQuantity)).ToArray(), result.IsReplay, result.OriginalCorrelationId);

    private static PurchaseReceiptAdjustmentTargetV1 RequireTarget(PurchaseReceiptAdjustmentTargetV1? target,
        Guid companyId, PurchaseReceiptReversalWriteV1 write)
    {
        if (target is null) throw new PurchasingServiceException(404, "not_found");
        if (target.CompanyId != companyId || target.OrderId != write.OrderId || target.AcceptanceId != write.AcceptanceId
            || target.LineId == Guid.Empty || target.GeneralLineId == Guid.Empty || target.ProductId == Guid.Empty
            || target.UnitId == Guid.Empty || string.IsNullOrWhiteSpace(target.ProductVersion)
            || !CanonicalVersion(target.ObservedOrderVersion)
            || !InventoryFutureAllocationTextV1.TryPositiveQuantity(target.OriginalAcceptedQuantity, out _)
            || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(target.AlreadyUndoneQuantity, out _)) throw Unavailable();
        return target;
    }

    private static void RequireResult(PurchaseReceiptAdjustmentResultV1 result, Guid companyId, PurchaseReceiptReversalWriteV1 write)
    {
        if (result is null || result.CompanyId != companyId || result.OrderId != write.OrderId
            || result.AcceptanceId != write.AcceptanceId || result.AdjustmentId != write.Request.AdjustmentId
            || result.LineId == Guid.Empty || result.GeneralLineId == Guid.Empty || string.IsNullOrWhiteSpace(result.OriginalCorrelationId)
            || !CanonicalVersion(result.OrderVersion)
            || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(result.NetOpenedQuantity, out _)
            || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(result.AllocatableQuantity, out _)
            || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(result.NewAcceptedQuantity, out _)
            || result.NewAcceptanceId != write.Request.ReplacementReceipt?.AcceptanceId
            || result.NewAcceptedQuantity != (write.Request.ReplacementReceipt?.AcceptedQuantity ?? "0")) throw Unavailable();
    }

    private static void RequireSupply(PurchaseReceiptAdjustmentSupplyV1 actual, PurchaseReceiptAdjustmentResultV1 adjusted,
        Guid company, PurchaseReceiptAdjustmentTargetV1 target)
    {
        if (actual.CompanyId != company || actual.OrderId != target.OrderId || actual.GeneralLineId != target.GeneralLineId
            || actual.OrderVersion != adjusted.OrderVersion || actual.AllocatableQuantity != adjusted.AllocatableQuantity) throw Unavailable();
    }

    private sealed class PostAdjustmentSupply(IPurchaseReceiptAdjustmentParticipantV1 owner) : IInventoryFutureSupplySourceV1
    {
        public PurchaseReceiptAdjustmentTargetV1? Target { get; set; }
        public async Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId,
            CancellationToken cancellationToken = default)
        {
            var target = Target;
            if (target is null || target.OrderId != orderId || target.GeneralLineId != lineId) throw Unavailable();
            var value = await owner.ReadSupplyAsync(orderId, lineId, cancellationToken) ?? throw Unavailable();
            if (value.CompanyId != target.CompanyId || value.OrderId != orderId || value.GeneralLineId != lineId
                || !InventoryFutureAllocationTextV1.TryNonNegativeQuantity(value.AllocatableQuantity, out _)) throw Unavailable();
            return new InventoryFutureSupplySourceAnswerV1(value.IsAllocatable ? InventoryFutureSupplySourceOutcomeV1.Allocatable : InventoryFutureSupplySourceOutcomeV1.NotAllocatable,
                new InventoryFutureSupplyLineV1(orderId, lineId, target.ProductId, target.ProductVersion, target.UnitId, value.AllocatableQuantity,
                    ObservedOrderVersion: Version(value.OrderVersion)));
        }
    }

    private static bool CanonicalVersion(string text) => InventoryFutureAllocationTextV1.TryVersion(text, out var version)
        && version.ToString(CultureInfo.InvariantCulture) == text;
    private static long Version(string text) => InventoryFutureAllocationTextV1.TryVersion(text, out var version)
        && version.ToString(CultureInfo.InvariantCulture) == text
        ? version : throw Unavailable();
    private static PurchasingServiceException Unavailable() => new PurchasingServiceException(503, PurchaseReceiptAdjustmentCodesV1.Unavailable);
    private static InventoryCommandMetadata Metadata(PurchasingWriteEnvelope envelope, string commandName) => new InventoryCommandMetadata(envelope.ActorId,
        envelope.PermissionCode, envelope.RoleCodes, envelope.CorrelationId, envelope.CausationId,
        InventoryChildKey(commandName, envelope.IdempotencyKey),
        envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent);
    internal static string InventoryChildKey(string commandName, string key) => commandName == PurchaseReceiptReversalApiV1.CommandName
        ? key : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(commandName + "\n" + key))).ToLowerInvariant();
    private async Task RecordPhysicalRefusalAsync(Exception exception, bool rolledBack)
    {
        if (InventoryNegativeStockAttemptEvidenceException.FindEvidence(exception) is null) return;
        if (!rolledBack) throw Unavailable();
        try { await InventoryNegativeStockAttemptRecording.AfterRollbackAsync(services, exception); }
        catch (Exception) { throw Unavailable(); }
    }
    private static async Task<bool> RollbackQuietlyAsync(IDbContextTransaction transaction)
    { try { await transaction.RollbackAsync(CancellationToken.None); return true; } catch { return false; } }
}
