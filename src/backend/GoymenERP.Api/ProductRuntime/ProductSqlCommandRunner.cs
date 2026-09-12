using System.Data;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.ProductRuntime;

public sealed class ProductSqlCommandRunner(ProductRuntimeOptions options, IServiceScopeFactory scopes,
    ProductCommandGateway gateway, IGroupSharedMasterAuthorizationService authority,
    ICorrelationContext correlation, IHttpContextAccessor http, IProductCommandAttemptObserver observer,
    ILogger<ProductSqlCommandRunner> logger) : IProductDraftCommandRunner
{
    public async Task<ProductMutationResponse> RunAsync(ProductPreparedCommand prepared, CancellationToken cancellationToken)
    {
        var request = http.HttpContext ?? throw new InvalidOperationException("Product commands require bound request metadata.");
        var keys = request.Request.Headers[ProductApiV1.IdempotencyHeader];
        if (!correlation.IsResolved || keys.Count != 1 || string.IsNullOrEmpty(keys[0])
            || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keys[0]!))) != prepared.Intent.KeyHash)
            throw new InvalidOperationException("Product request metadata does not match its frozen intent.");
        var key = keys[0]!;
        var attempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await gateway.RefreshAsync(prepared, cancellationToken);
            if (current.Replay is not null)
            {
                request.Response.Headers["Idempotency-Replayed"] = "true";
                request.Response.Headers["Idempotency-Original-Correlation-Id"] = current.OriginalCorrelationId;
                return current.Replay;
            }
            if (attempts++ >= options.MaximumAttempts)
                throw new ProductOperationException("product_write_contention", StatusCodes.Status503ServiceUnavailable);
            try
            {
                return await ExecuteAttemptAsync(current, key, cancellationToken);
            }
            catch (RetryableProductAttemptException)
            {
                // ExecuteAttemptAsync has confirmed rollback and disposed its whole
                // borrowed pair. Refresh the actual winner even after the last attempt.
            }
            catch (ProductVersionConflictException)
            {
                // Both early and late SQL conflicts leave the attempt before their
                // version metadata is disclosed. A changed/revoked grant masks it.
                _ = await ReauthorizeAsync(current.Context, cancellationToken);
                // A concurrent identical edit may already have committed this
                // logical command. Resolve its terminal after rollback/disposal;
                // do not turn the successful same-key command into a stale edit.
                var winner = await gateway.RefreshAsync(current, cancellationToken);
                if (winner.Replay is not null)
                {
                    request.Response.Headers["Idempotency-Replayed"] = "true";
                    request.Response.Headers["Idempotency-Original-Correlation-Id"] = winner.OriginalCorrelationId;
                    return winner.Replay;
                }
                throw;
            }
            catch (ProductOperationException exception) when (exception.StatusCode is 404 or 409)
            {
                _ = await ReauthorizeAsync(current.Context, cancellationToken);
                throw;
            }
        }
    }

    private async Task<ProductMutationResponse> ExecuteAttemptAsync(ProductPreparedCommand current, string key,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var productFlushed = false;
        var committed = false;
        var committing = false;
        try
        {
            await using var product = await ProductDraftParticipant.EnlistAsync(connection, transaction, current.Context, cancellationToken,
                onStaged: token => observer.ObserveAsync(ProductCommandAttemptStage.ProductStaged, current.Context, token));
            await using var audit = await GroupSharedAuditSession.EnlistAsync(scope.ServiceProvider.GetRequiredService<AuditDbContext>(),
                connection, transaction, current.Context, cancellationToken);
            await using var terminal = await GroupSharedCommandTerminalSession.EnlistAsync(connection, transaction,
                current.Context, current.Intent.KeyHash, current.Intent.RequestHash, cancellationToken);
            var result = await product.ExecuteAsync(current.Intent, cancellationToken);
            productFlushed = true;
            await observer.ObserveAsync(ProductCommandAttemptStage.ProductFlushed, current.Context, cancellationToken);
            audit.Enqueue(new GroupSharedAuditEventDraft(current.Context, result.ProductId, result.EventType,
                result.VersionBefore, result.VersionAfter, result.BeforeJson, result.AfterJson, result.Reason,
                correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, correlation.CorrelationId,
                correlation.CausationId, key, AuditAiInvolvement.None, null));
            if (audit.ProductId != result.ProductId || audit.IdempotencyKeyHash != current.Intent.KeyHash)
                throw new InvalidOperationException("The canonical audit does not match the Product result and intent.");
            terminal.Stage(GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(current.Context,
                current.Intent.KeyHash, current.Intent.RequestHash), result.StatusCode, result.ResponseJson, correlation.CorrelationId, 1));
            var fresh = await ReauthorizeAsync(current.Context, cancellationToken);
            await audit.FlushAsync(fresh, cancellationToken);
            await observer.ObserveAsync(ProductCommandAttemptStage.AuditFlushed, current.Context, cancellationToken);
            await terminal.FlushAsync(fresh, cancellationToken);
            await observer.ObserveAsync(ProductCommandAttemptStage.TerminalFlushed, current.Context, cancellationToken);
            await observer.ObserveAsync(ProductCommandAttemptStage.BeforeCommit, current.Context, cancellationToken);
            _ = await ReauthorizeAsync(current.Context, cancellationToken);
            if (!productFlushed || !audit.IsFlushed || !terminal.IsFlushed)
                throw new InvalidOperationException("All three owners must flush before the only commit path.");
            committing = true;
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return result.Response;
        }
        catch (Exception exception)
        {
            var rollbackConfirmed = false;
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    rollbackConfirmed = true;
                }
                catch (Exception cleanup) when (cleanup is SqlException or InvalidOperationException)
                {
                    logger.LogWarning("Product rollback could not confirm completion. CorrelationId={CorrelationId}", correlation.CorrelationId);
                }
            }
            // Neither retry nor detailed error metadata is safe after an unconfirmed
            // transaction outcome. The later same-key request owns recovery.
            if (!rollbackConfirmed) throw new ProductTransactionRecoveryException(exception);
            if (!CanRetryAfterRollback(rollbackConfirmed, committing, cancellationToken.IsCancellationRequested)) throw;
            var retry = exception is GroupSharedCommandTerminalAlreadyExistsException
                || !productFlushed && exception is DbUpdateConcurrencyException concurrency
                    && concurrency.Entries.Count > 0 && concurrency.Entries.All(entry => entry.Entity is ProductCodeCounter)
                || !productFlushed && exception is DbUpdateException update
                    && update.InnerException is SqlException { Number: 2601 or 2627 }
                    && update.Entries.Count > 0 && update.Entries.All(entry => entry.Entity is ProductCodeCounter or ProductCodeRegistration or ProductRow);
            if (!retry) throw;
            throw new RetryableProductAttemptException(exception);
        }
    }

    public static bool CanRetryAfterRollback(bool rollbackConfirmed, bool commitStarted, bool cancellationRequested) =>
        rollbackConfirmed && !commitStarted && !cancellationRequested;

    private async Task<GroupSharedMasterCommandContext> ReauthorizeAsync(GroupSharedMasterCommandContext expected,
        CancellationToken cancellationToken)
    {
        var current = GroupSharedMasterCommandContext.FromServerDecision(await authority.GetSnapshotAsync(
            GroupSharedMasterKind.Product, expected.Operation, cancellationToken), expected.Operation);
        if (current.LocalActorId != expected.LocalActorId || current.OriginCompanyId != expected.OriginCompanyId
            || current.AuthorityRevision != expected.AuthorityRevision)
            throw new UnauthorizedAccessException("Product authority changed before commit.");
        return current;
    }

    private sealed class RetryableProductAttemptException(Exception inner)
        : Exception("The fully rolled-back Product attempt may be freshly resolved.", inner);
    private sealed class ProductTransactionRecoveryException(Exception inner)
        : Exception("Product transaction completion could not be confirmed.", inner);
}
