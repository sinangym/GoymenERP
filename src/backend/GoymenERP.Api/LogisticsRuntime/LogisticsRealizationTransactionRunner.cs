using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.LogisticsRuntime;

internal sealed class LogisticsRealizationTransactionRunner(
    LogisticsDbContext logistics,
    IServiceProvider services,
    TimeProvider clock) : ILogisticsRealizationTransactionRunnerV1
{
    public Task<T> ExecuteShipmentAsync<T>(Guid companyId,
        Func<ILogisticsShipmentStockV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(companyId, operation, beforeCommit, cancellationToken);

    public Task<T> ExecutePlanExcessShipmentAsync<T>(Guid companyId,
        Func<ILogisticsShipmentStockV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default) =>
        ExecuteAsync(companyId, operation, beforeCommit, cancellationToken, serializable: true);

    public async Task<T> ExecutePlanExcessAsync<T>(Guid companyId, Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        Require(companyId, operation, beforeCommit);
        var company = services.GetService<ICompanyContext>();
        var audit = services.GetService<IAuditTransactionWriterV1>();
        // Entry refusal is outside owned cleanup: never clear a caller's dirty state, audit or borrowed transaction.
        if (company is null || !company.IsResolved || company.CompanyId != companyId || audit is null
            || System.Transactions.Transaction.Current is not null || logistics.Database.CurrentTransaction is not null
            || !logistics.Database.IsRelational() || logistics.ChangeTracker.HasChanges() || audit.HasPendingDrafts)
            throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
        async Task Fence()
        {
            if (!company.IsResolved || company.CompanyId != companyId) throw new LogisticsInstructionServiceException(403, "forbidden");
            await beforeCommit(cancellationToken);
            if (!company.IsResolved || company.CompanyId != companyId) throw new LogisticsInstructionServiceException(403, "forbidden");
        }
        await using var owned = await logistics.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await Fence();
            var result = await operation(cancellationToken);
            // Includes native SaveChanges and its Audit interceptor. Callback is the captured owner
            // company/actor/permission fence, including the host's live delegation decorator.
            await Fence();
            if (audit.HasPendingDrafts)
            {
                await audit.WritePendingAsync(owned.GetDbTransaction(), cancellationToken);
                await Fence();
            }
            if (audit.HasPendingDrafts) throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
            await Fence();
            await owned.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            var rolledBack = await RollBackAsync(owned, CancellationToken.None);
            try { await owned.DisposeAsync(); }
            finally
            {
                logistics.ChangeTracker.Clear();
                audit.DiscardPending();
            }
            if (!rolledBack) throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
            throw;
        }
    }

    public Task<T> ExecuteDeliveryAsync<T>(Guid companyId,
        Func<CancellationToken, Task<T>> operation, Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default) => ExecuteDeliveryCoreAsync(companyId, operation, beforeCommit, cancellationToken, serializable: false);

    private async Task<T> ExecuteDeliveryCoreAsync<T>(Guid companyId,
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken, bool serializable)
    {
        Require(companyId, operation, beforeCommit);
        await using var transaction = await logistics.Database.BeginTransactionAsync(serializable ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await beforeCommit(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            var rolledBack = await RollBackAsync(transaction, CancellationToken.None);
            logistics.ChangeTracker.Clear();
            if (!rolledBack && serializable) throw new LogisticsInstructionServiceException(503, LogisticsPlanExcessV1.Unavailable);
            throw;
        }
    }

    private async Task<T> ExecuteAsync<T>(Guid companyId,
        Func<ILogisticsShipmentStockV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken, bool serializable = false)
    {
        Require(companyId, operation, beforeCommit);
        await using var transaction = await logistics.Database.BeginTransactionAsync(serializable ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, cancellationToken);
        await using var stock = new ShipmentStock(
            logistics.Database.GetDbConnection(),
            transaction.GetDbTransaction(),
            companyId,
            services,
            clock);
        try
        {
            var result = await operation(stock, cancellationToken);
            await beforeCommit(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            var rolledBack = await RollBackAsync(transaction, CancellationToken.None);
            logistics.ChangeTracker.Clear();
            RequirePhysicalGuardRollback(rolledBack, exception);
            var cause = stock.PhysicalRefusal ?? exception;
            if (InventoryNegativeStockAttemptEvidenceException.FindEvidence(cause) is not null)
            {
                if (!rolledBack) throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
                try { await InventoryNegativeStockAttemptRecording.AfterRollbackAsync(services, cause); }
                catch (Exception) { throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable); }
            }
            throw;
        }
    }

    private static bool IsPhysicalGuardConflict(InventoryDefiniteShipmentException error) => error.Status == 409
        && error.Code == InventoryDefiniteShipmentCodesV1.ConcurrentChange
        && error.InnerException is InventoryStoreException { Code: InventoryStoreCodes.ConcurrentChange };

    internal static Exception MapShipmentRefusal(InventoryDefiniteShipmentException error) => IsPhysicalGuardConflict(error)
        ? new LogisticsStoreException(LogisticsStoreCodes.ConcurrentChange,
            "Fiziksel stok doğrulaması eşzamanlı değişti; sevk işlemi geri alınmalıdır.", error)
        : new LogisticsInstructionServiceException(error.Status, error.Code, innerException: error,
            detail: InventoryPhysicalStockRefusalProblem.Apply(error.Status, error, new Dictionary<string, object?>()));

    internal static void RequirePhysicalGuardRollback(bool rolledBack, Exception error)
    {
        if (!rolledBack && error is LogisticsStoreException
                { Code: LogisticsStoreCodes.ConcurrentChange, InnerException: InventoryDefiniteShipmentException physical }
            && IsPhysicalGuardConflict(physical))
            throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
    }

    private static void Require(Guid companyId, Delegate operation, Func<CancellationToken, Task> beforeCommit)
    {
        if (companyId == Guid.Empty)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
        }

        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(beforeCommit);
    }

    private static async Task<bool> RollBackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
            return true;
        }
        catch { return false; }
    }

    private sealed class ShipmentStock(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        IServiceProvider services,
        TimeProvider clock) : ILogisticsShipmentStockV1, IAsyncDisposable
    {
        private IInventoryDefiniteShipmentParticipantV1? participant;
        public Exception? PhysicalRefusal { get; private set; }

        public async Task<LogisticsShipmentAllocationV1?> FindAllocationAsync(
            Guid salesOrderId,
            Guid salesOrderLineId,
            Guid allocationId,
            CancellationToken cancellationToken = default)
        {
            InventoryPhysicalAllocationReadsV1 page;
            try
            {
                page = await services.GetRequiredService<IInventoryPhysicalAllocationReadsV1>()
                    .SearchAsync(new InventoryPhysicalAllocationReadQueryV1(salesOrderId, salesOrderLineId, 100), cancellationToken);
            }
            catch (InventoryPhysicalAllocationReadException exception) when (exception.StatusCode is 401 or 403)
            {
                throw new LogisticsInstructionServiceException(exception.StatusCode, exception.Code);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
            }

            var matches = page.Items.Where(item => item.Allocation.AllocationId == allocationId).ToArray();
            if (matches.Length == 0)
            {
                if (page.SearchWindowFull)
                {
                    throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
                }

                return null;
            }

            if (matches.Length != 1)
            {
                throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
            }

            var allocation = matches[0].Allocation;
            if (allocation.CompanyId != companyId
                || allocation.DemandKind != InventoryAllocationDemandKindsV1.SalesOrderLine
                || allocation.DemandId != salesOrderId
                || allocation.DemandLineId != salesOrderLineId
                || allocation.LabelId == Guid.Empty
                || allocation.ProductId == Guid.Empty)
            {
                throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);
            }

            return new LogisticsShipmentAllocationV1(
                allocation.CompanyId,
                allocation.AllocationId,
                allocation.LabelId,
                allocation.DemandId,
                allocation.DemandLineId,
                allocation.ProductId);
        }

        public async Task<LogisticsShipmentStockResultV1> StageAsync(
            LogisticsShipmentStockRequestV1 request,
            LogisticsWriteEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            participant ??= await InventoryDefiniteShipmentParticipant.EnlistAsync(
                connection,
                transaction,
                companyId,
                services.GetRequiredService<ISettingsPrerequisiteProvider>(),
                cancellationToken,
                clock);
            try
            {
                var result = await participant.StageAsync(
                    new InventoryDefiniteShipmentRequestV1(
                        request.CompanyId,
                        request.ShipmentId,
                        request.LoadingInstructionId,
                        request.LoadingInstructionLineId,
                        request.AllocationId,
                        request.ExpectedAllocationVersion,
                        request.LabelId,
                        request.ExpectedLabelVersion,
                        request.MovementId,
                        request.QuantityKg,
                        request.OccurredAtUtc,
                        // AK-4 (KLN-D119): a piece line's shipment names the pieces leaving the label.
                        request.PieceCount),
                    new InventoryCommandMetadata(
                        envelope.ActorId,
                        envelope.PermissionCode,
                        envelope.RoleCodes,
                        envelope.CorrelationId,
                        envelope.CausationId,
                        envelope.IdempotencyKey,
                        envelope.SourceChannel,
                        envelope.ClientIpAddress,
                        envelope.UserAgent),
                    cancellationToken);
                var staged = new LogisticsShipmentStockResultV1(
                    result.CompanyId,
                    result.ShipmentId,
                    result.AllocationId,
                    result.LabelId,
                    result.MovementId,
                    result.OutboxMessageId,
                    result.IsReplay,
                    result.OriginalCorrelationId);
                if (services.GetService<ILogisticsRealizationStageObserver>() is { } observer)
                {
                    await observer.ObserveAsync(LogisticsRealizationStage.InventoryStaged, cancellationToken);
                }

                return staged;
            }
            catch (InventoryDefiniteShipmentException exception)
            {
                PhysicalRefusal = exception;
                throw MapShipmentRefusal(exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (participant is not null)
            {
                await participant.DisposeAsync();
            }
        }
    }
}

internal enum LogisticsRealizationStage
{
    InventoryStaged = 1,
}

internal interface ILogisticsRealizationStageObserver
{
    Task ObserveAsync(LogisticsRealizationStage stage, CancellationToken cancellationToken);
}
