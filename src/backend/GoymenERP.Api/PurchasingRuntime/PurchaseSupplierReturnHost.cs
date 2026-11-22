using System.Data.Common;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// ALC-S5 (B-19; SAT-D306): the supplier return host. It owns ONE transaction on the scoped Purchasing context: the return row, its
/// attempt and its audit (the store's single save) and Inventory's exit (stock, label, movement and outbox rows staged by the
/// existing <see cref="InventorySupplierReturnExitParticipant"/>) commit together or not at all. The participant is enlisted lazily,
/// only when the service asks for the exit (a pending cost exception moves no stock, SAT-D393), at most once per transaction, and is
/// disposed after the commit or the rollback. An Inventory refusal keeps its own status and code.
/// </summary>
internal sealed class PurchaseSupplierReturnHost(
    PurchasingDbContext purchasing,
    IServiceProvider services,
    TimeProvider clock) : IPurchaseSupplierReturnTransactionV1
{
    public async Task<T> ExecuteAsync<T>(Guid companyId,
        Func<IPurchaseSupplierReturnExitV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new InvalidOperationException("Supplier return requires a company.");
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        try
        {
            await using var transaction = await purchasing.Database.BeginTransactionAsync(cancellationToken);
            var exit = new Exit(companyId, purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(),
                services.GetRequiredService<ISettingsPrerequisiteProvider>(), clock,
                services.GetRequiredService<PurchaseSupplierReturnPurchaseSourceReader>());
            try
            {
                var result = await operation(exit, cancellationToken);
                await beforeCommit(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception)
            {
                var rolledBack = await RollbackQuietlyAsync(transaction);
                purchasing.ChangeTracker.Clear();
                if (InventoryNegativeStockAttemptEvidenceException.FindEvidence(exception) is not null)
                {
                    if (!rolledBack) throw new PurchasingServiceException(503, InventorySupplierReturnExitCodesV1.Unavailable);
                    try { await InventoryNegativeStockAttemptRecording.AfterRollbackAsync(services, exception); }
                    catch (Exception) { throw new PurchasingServiceException(503, InventorySupplierReturnExitCodesV1.Unavailable); }
                }
                throw;
            }
            finally
            {
                await exit.DisposeAsync();
            }
        }
        catch (InventorySupplierReturnExitException exception)
        {
            if (exception.Code == InventorySupplierReturnExitCodesV1.CostAllocationRequired)
                throw new PurchasingServiceException(409, PurchaseSupplierReturnCostAllocationContractV1.Required,
                    [new PurchasingFieldError("costAllocation", PurchaseSupplierReturnCostAllocationContractV1.Required)], innerException: exception);
            if (exception.Code == InventorySupplierReturnExitCodesV1.QuantityCostUnreadable)
                // SAT-D393: the stock value cannot be read; the caller is told to take the exception path, nothing was written.
                throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.CostRequired,
                    [new PurchasingFieldError("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.CostRequired)], innerException: exception);
            throw new PurchasingServiceException(exception.Status, exception.Code, innerException: exception,
                detail: InventoryPhysicalStockRefusalProblem.Apply(exception.Status, exception, new Dictionary<string, object?>()));
        }
    }

    private static async Task<bool> RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); return true; }
        catch { return false; }
    }

    /// <summary>Adapts the Purchasing exit request to Inventory's port; the source block text is parsed strictly (Free or Blocked).</summary>
    private sealed class Exit(Guid companyId, DbConnection connection, DbTransaction transaction,
        ISettingsPrerequisiteProvider settings, TimeProvider clock, PurchaseSupplierReturnPurchaseSourceReader purchases)
        : IPurchaseSupplierReturnExitV1, IAsyncDisposable
    {
        private InventorySupplierReturnExitParticipant? participant;

        public async Task<PurchaseSupplierReturnExitResultV1> StageExitAsync(PurchaseSupplierReturnExitRequestV1 request,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(envelope);
            if (request.CompanyId != companyId) throw new InvalidOperationException("Supplier return exit is for another company.");
            if (participant is not null) throw new InvalidOperationException("A supplier return stages its stock exit once.");
            if (!TryParseBlock(request.SourceBlock, out var block))
                throw new InvalidOperationException("Supplier return source block is not recognised.");
            var purchase = await purchases.ReadAsync(companyId, request.PurchaseOrderId, request.GoodsAcceptanceId,
                request.ReturnId, cancellationToken);
            participant = await InventorySupplierReturnExitParticipant.EnlistAsync(connection, transaction, companyId, settings,
                cancellationToken, clock);
            if (request.LineageMode == PurchaseSupplierReturnLineageModeV1.Lineage && request.LineageParts is { } parts)
            {
                // AK-8b (SAT-D433): one exit per receipt of the lineage, all under ONE Inventory command in this transaction.
                if (request.SourceLabelId is not { } label) throw new InvalidOperationException("A lineage return names its source label.");
                var lineage = await participant.StageLineageAsync(new InventorySupplierReturnLineageExitRequestV1(request.CompanyId,
                    request.ReturnId, label, block, request.QuantityKg, request.PieceCount, request.OccurredAtUtc,
                    parts.Select(part => new InventorySupplierReturnLineagePartV1(part.GoodsAcceptanceId, part.ReturnLineId,
                        part.InventoryMovementId, PurchaseSupplierReturnLineagePartsV1.Kg(part.QuantityKg), part.PieceCount,
                        UnitCost(part.UnitCostTryPerKg), Selection(part))).ToArray()), Metadata(envelope), cancellationToken);
                var own = lineage.Parts.SingleOrDefault(part => part.GoodsAcceptanceId == request.GoodsAcceptanceId)
                    ?? throw new InvalidOperationException("The lineage exit did not answer the return's own receipt.");
                return new PurchaseSupplierReturnExitResultV1(own.InventoryMovementId, lineage.LabelId, request.QuantityKg, lineage.IsReplay);
            }

            var staged = await participant.StageAsync(new InventorySupplierReturnExitRequestV1(request.CompanyId, request.ReturnId,
                request.ReturnLineId, request.GoodsAcceptanceId, request.InventoryMovementId, block, request.QuantityKg,
                // SAT-D456: an amount-measured quantity return with a readable average names no unit cost (empty → none).
                request.UnitCostTryPerKg.Length == 0 ? null : request.UnitCostTryPerKg, request.OccurredAtUtc,
                PurchaseSupplierReturnCostSources.Map(request.CostAllocation),
                PurchaseSupplierReturnCostSources.Map(purchase), request.UndoQuantity,
                PieceCount: request.SourceLabelId is null ? null : request.PieceCount,
                SourceLabelId: request.SourceLabelId,
                LineageMode: request.LineageMode switch
                {
                    null => null,
                    PurchaseSupplierReturnLineageModeV1.Lineage => InventorySupplierReturnLineageModeV1.Lineage,
                    PurchaseSupplierReturnLineageModeV1.Quantity => InventorySupplierReturnLineageModeV1.Quantity,
                    _ => throw new InvalidOperationException("Supplier return lineage mode is not recognised."),
                },
                // A quantity return (or an earlier single-receipt lineage return) carries at most one part: its explicit choice.
                LineageSelection: request.LineageParts is { Count: 1 } single ? Selection(single[0]) : null,
                ApprovedCostException: request.ApprovedCostException,
                SupplierReturnAmountTry: request.SupplierReturnAmountTry?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                Metadata(envelope), cancellationToken);
            return new PurchaseSupplierReturnExitResultV1(staged.InventoryMovementId, staged.LabelId, staged.QuantityKg, staged.IsReplay,
                staged.OriginalUnitCostTryPerKg);
        }

        private static string UnitCost(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        private static InventorySupplierReturnPartSelectionV1? Selection(PurchaseSupplierReturnLineagePartV1 part) =>
            part.PendingKg is null || part.Invoiced is null ? null : new InventorySupplierReturnPartSelectionV1(part.PendingKg,
                part.Invoiced.Select(item => new InventorySupplierReturnInvoicedSelectionV1(item.SourceEventId, item.QuantityKg)).ToArray());

        public async ValueTask DisposeAsync()
        {
            if (participant is null) return;
            try { await participant.DisposeAsync(); }
            catch { /* Disposal never hides the operation's own outcome. */ }
            participant = null;
        }

        private static bool TryParseBlock(string? value, out BlockState block)
        {
            block = default;
            if (string.Equals(value, nameof(BlockState.Free), StringComparison.Ordinal)) block = BlockState.Free;
            else if (string.Equals(value, nameof(BlockState.Blocked), StringComparison.Ordinal)) block = BlockState.Blocked;
            else return false;
            return true;
        }

        private static InventoryCommandMetadata Metadata(PurchasingWriteEnvelope envelope) => new(
            envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent);
    }
}
