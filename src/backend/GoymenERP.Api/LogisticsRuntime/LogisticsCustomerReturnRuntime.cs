using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
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
        var audit = RequireOwnedEntry(companyId);
        var actor = user.UserId!;
        var transaction = await logistics.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var disposed = false;
        try
        {
            await FenceAsync(companyId, actor, beforeCommit, cancellationToken);
            var result = await work(cancellationToken);
            await FenceAsync(companyId, actor, beforeCommit, cancellationToken);
            if (audit.HasPendingDrafts)
            {
                await audit.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
                await FenceAsync(companyId, actor, beforeCommit, cancellationToken);
            }
            if (audit.HasPendingDrafts) throw Unavailable();
            await FenceAsync(companyId, actor, beforeCommit, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            disposed = true;
            if (!await AbortAsync(transaction, null, audit)) throw Unavailable();
            throw;
        }
        finally { if (!disposed) await transaction.DisposeAsync(); }
    }

    public async Task<T> ExecuteApprovalAsync<T>(Guid companyId,
        Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task<T>> work,
        Func<ILogisticsCustomerReturnStockV1, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        Require(companyId, work, beforeCommit);
        var audit = RequireOwnedEntry(companyId);
        var actor = user.UserId!;
        var transaction = await logistics.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var stock = new CustomerReturnStock(logistics.Database.GetDbConnection(), transaction.GetDbTransaction(),
            companyId, services, company, user, authority, clock);
        var disposed = false;
        try
        {
            await FenceAsync(companyId, actor, token => beforeCommit(stock, token), cancellationToken);
            var result = await work(stock, cancellationToken);
            await FenceAsync(companyId, actor, token => beforeCommit(stock, token), cancellationToken);
            if (audit.HasPendingDrafts)
            {
                await audit.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
                await FenceAsync(companyId, actor, token => beforeCommit(stock, token), cancellationToken);
            }
            if (audit.HasPendingDrafts) throw Unavailable();
            await FenceAsync(companyId, actor, token => beforeCommit(stock, token), cancellationToken);
            // The Inventory participant has already saved its own audit in this same borrowed transaction.
            // Finish its context before commit; a disposal fault must not release a committed success.
            await stock.DisposeAsync();
            // Native callback only revalidates the captured permission and stock authority; no new participant is enlisted.
            await FenceAsync(companyId, actor, token => beforeCommit(stock, token), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            disposed = true;
            if (!await AbortAsync(transaction, stock, audit)) throw Unavailable();
            throw;
        }
        finally { if (!disposed) await transaction.DisposeAsync(); }
    }

    private static void Require(Guid companyId, Delegate work, Delegate beforeCommit)
    {
        if (companyId == Guid.Empty) throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.SourceInvalid);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(beforeCommit);
    }

    private IAuditTransactionWriterV1 RequireOwnedEntry(Guid companyId)
    {
        // Shared Request/Reject/Approve/terminal/recovery boundary. Refusal is OUTSIDE owned cleanup:
        // callers' ambient/borrowed handles, unsaved entities and pending audit drafts belong to them.
        var audit = services.GetService<IAuditTransactionWriterV1>();
        if (audit is null || !company.IsResolved || company.CompanyId != companyId
            || !user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId) || !user.CanAccessCompany(companyId)
            || System.Transactions.Transaction.Current is not null || !logistics.Database.IsRelational()
            || logistics.Database.CurrentTransaction is not null || logistics.ChangeTracker.HasChanges() || audit.HasPendingDrafts)
            throw Unavailable();
        return audit;
    }

    private async Task FenceAsync(Guid companyId, string actor, Func<CancellationToken, Task> callback, CancellationToken token)
    {
        bool Current() => company.IsResolved && company.CompanyId == companyId && user.IsAuthenticated
            && user.UserId == actor && user.CanAccessCompany(companyId);
        if (!Current()) throw new LogisticsCustomerReturnServiceException(403, "forbidden");
        await callback(token);
        if (!Current()) throw new LogisticsCustomerReturnServiceException(403, "forbidden");
    }

    private async Task<bool> AbortAsync(IDbContextTransaction transaction, CustomerReturnStock? stock, IAuditTransactionWriterV1 audit)
    {
        var clean = true;
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch { clean = false; }
        try { if (stock is not null) await stock.DisposeAsync(); }
        catch { clean = false; }
        finally
        {
            try { await transaction.DisposeAsync(); }
            catch { clean = false; }
            finally
            {
                try { logistics.ChangeTracker.Clear(); }
                finally { audit.DiscardPending(); }
            }
        }
        return clean;
    }

    private static LogisticsCustomerReturnServiceException Unavailable() =>
        new(503, LogisticsCustomerReturnCodesV1.InventoryUnavailable);

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
            if (!measured.IsGranted || !measured.IsCompanyWide
                || !user.IsAuthenticated || !string.Equals(user.UserId, actorId, StringComparison.Ordinal)
                || !company.IsResolved || company.CompanyId != companyId || !user.CanAccessCompany(companyId))
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
                    (request.QuantityKg ?? request.Quantity).ToString(System.Globalization.CultureInfo.InvariantCulture), request.OccurredAtUtc, request.PieceCount),
                    new InventoryCommandMetadata(envelope.ActorId, ErpPermissions.InventoryPhysicalAllocationWrite,
                        roleCodes, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey,
                        envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent), cancellationToken);
                return new(result.CompanyId, result.CustomerReturnId, result.SourceShipmentId,
                    result.InventoryMovementId, result.OriginalShipmentMovementId, result.OriginalLabelId,
                    decimal.Parse(result.QuantityKg, System.Globalization.CultureInfo.InvariantCulture),
                    result.IsReplay, result.OriginalCorrelationId, result.PieceCount);
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
