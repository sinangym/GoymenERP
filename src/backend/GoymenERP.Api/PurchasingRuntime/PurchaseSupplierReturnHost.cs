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
                services.GetRequiredService<ISettingsPrerequisiteProvider>(), clock);
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
        ISettingsPrerequisiteProvider settings, TimeProvider clock) : IPurchaseSupplierReturnExitV1, IAsyncDisposable
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
            participant = await InventorySupplierReturnExitParticipant.EnlistAsync(connection, transaction, companyId, settings,
                cancellationToken, clock);
            var staged = await participant.StageAsync(new InventorySupplierReturnExitRequestV1(request.CompanyId, request.ReturnId,
                request.ReturnLineId, request.GoodsAcceptanceId, request.InventoryMovementId, block, request.QuantityKg,
                request.UnitCostTryPerKg, request.OccurredAtUtc), Metadata(envelope), cancellationToken);
            return new PurchaseSupplierReturnExitResultV1(staged.InventoryMovementId, staged.LabelId, staged.QuantityKg, staged.IsReplay);
        }

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
