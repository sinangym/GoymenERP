using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.LogisticsRuntime;

internal sealed class LogisticsCustomerReturnScope(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority) : ILogisticsCustomerReturnScopeV1
{
    public async Task<LogisticsCustomerReturnScopeV1> RequireAsync(string permission,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
            throw new LogisticsCustomerReturnServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new LogisticsCustomerReturnServiceException(403, "forbidden");
        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
            throw new LogisticsCustomerReturnServiceException(503, "permission_unregistered");

        var companyId = company.CompanyId;
        var actorId = user.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
            throw new LogisticsCustomerReturnServiceException(403, "forbidden");
        return new(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    public async Task RequireStillAsync(LogisticsCustomerReturnScopeV1 scope, string permission,
        CancellationToken cancellationToken = default)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new LogisticsCustomerReturnServiceException(403, "forbidden");
    }

    private bool Matches(Guid companyId, string actorId) => user.IsAuthenticated
        && string.Equals(user.UserId, actorId, StringComparison.Ordinal)
        && company.IsResolved && company.CompanyId == companyId && user.CanAccessCompany(companyId);
}

internal sealed class LogisticsCustomerReturnTransactionRunner(LogisticsDbContext logistics, IServiceProvider services,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority, TimeProvider clock)
    : ILogisticsCustomerReturnTransactionRunnerV1
{
    public async Task<T> ExecuteLogisticsAsync<T>(Guid companyId, Func<CancellationToken, Task<T>> work,
        Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
    {
        Require(companyId, work, beforeCommit);
        await using var transaction = await logistics.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            await beforeCommit(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            logistics.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<T> ExecuteApprovalAsync<T>(Guid companyId,
        Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task<T>> work,
        Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        Require(companyId, work, beforeCommit);
        await using var transaction = await logistics.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var stock = new CustomerReturnStock(logistics.Database.GetDbConnection(), transaction.GetDbTransaction(),
            companyId, services, company, user, authority, clock);
        try
        {
            var result = await work(stock, cancellationToken);
            await beforeCommit(stock, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            logistics.ChangeTracker.Clear();
            throw;
        }
    }

    private static void Require(Guid companyId, Delegate work, Delegate beforeCommit)
    {
        if (companyId == Guid.Empty) throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.SourceInvalid);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(beforeCommit);
    }

    private static async Task RollbackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch { /* Preserve the operation failure. */ }
    }

    private sealed class CustomerReturnStock(DbConnection connection, DbTransaction transaction, Guid companyId,
        IServiceProvider services, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, TimeProvider clock) : ILogisticsCustomerReturnStockV1, IAsyncDisposable
    {
        private readonly string actorId = user.UserId ?? string.Empty;
        private IReadOnlyCollection<string> roleCodes = [];
        private IInventoryCustomerReturnParticipantV1? participant;

        public async Task RequireStillAsync(CancellationToken cancellationToken = default)
        {
            if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(actorId)
                || !string.Equals(user.UserId, actorId, StringComparison.Ordinal)
                || !company.IsResolved || company.CompanyId != companyId || !user.CanAccessCompany(companyId))
                throw new LogisticsCustomerReturnServiceException(403, "forbidden");
            var measured = await authority.RevalidatePermissionScopeAsync(
                ErpPermissions.InventoryPhysicalAllocationWrite, companyId, cancellationToken);
            if (!measured.IsGranted || !measured.IsCompanyWide)
                throw new LogisticsCustomerReturnServiceException(403, "forbidden");
            roleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)];
        }

        public async Task<LogisticsCustomerReturnStockResultV1> StageAsync(LogisticsCustomerReturnStockRequestV1 request,
            LogisticsWriteEnvelope envelope, CancellationToken cancellationToken = default)
        {
            participant ??= await InventoryCustomerReturnParticipant.EnlistAsync(connection, transaction, companyId,
                services.GetRequiredService<ISettingsPrerequisiteProvider>(), cancellationToken, clock);
            try
            {
                var result = await participant.StageAsync(new InventoryCustomerReturnRequestV1(
                    request.CompanyId, request.CustomerReturnId, request.SourceShipmentId,
                    request.LoadingInstructionLineId, request.InventoryMovementId,
                    request.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), request.OccurredAtUtc),
                    new InventoryCommandMetadata(envelope.ActorId, ErpPermissions.InventoryPhysicalAllocationWrite,
                        roleCodes, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey,
                        envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent), cancellationToken);
                return new(result.CompanyId, result.CustomerReturnId, result.SourceShipmentId,
                    result.InventoryMovementId, result.OriginalShipmentMovementId, result.OriginalLabelId,
                    decimal.Parse(result.QuantityKg, System.Globalization.CultureInfo.InvariantCulture),
                    result.IsReplay, result.OriginalCorrelationId);
            }
            catch (InventoryCustomerReturnException exception)
            {
                throw new LogisticsCustomerReturnServiceException(exception.Status, exception.Code);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (participant is not null) await participant.DisposeAsync();
        }
    }
}
