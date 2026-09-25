using System.Data.Common;
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

    public async Task<T> ExecuteDeliveryAsync<T>(Guid companyId,
        Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        Require(companyId, operation, beforeCommit);
        await using var transaction = await logistics.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await beforeCommit(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollBackAsync(transaction, cancellationToken);
            logistics.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<T> ExecuteAsync<T>(Guid companyId,
        Func<ILogisticsShipmentStockV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken)
    {
        Require(companyId, operation, beforeCommit);
        await using var transaction = await logistics.Database.BeginTransactionAsync(cancellationToken);
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
        catch
        {
            await RollBackAsync(transaction, cancellationToken);
            logistics.ChangeTracker.Clear();
            throw;
        }
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

    private static async Task RollBackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // Preserve the operation failure; disposing the transaction remains mandatory.
        }
    }

    private sealed class ShipmentStock(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        IServiceProvider services,
        TimeProvider clock) : ILogisticsShipmentStockV1, IAsyncDisposable
    {
        private IInventoryDefiniteShipmentParticipantV1? participant;

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
                        request.OccurredAtUtc),
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
                return new LogisticsShipmentStockResultV1(
                    result.CompanyId,
                    result.ShipmentId,
                    result.AllocationId,
                    result.LabelId,
                    result.MovementId,
                    result.OutboxMessageId,
                    result.IsReplay,
                    result.OriginalCorrelationId);
            }
            catch (InventoryDefiniteShipmentException exception)
            {
                throw new LogisticsInstructionServiceException(exception.Status, exception.Code);
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
