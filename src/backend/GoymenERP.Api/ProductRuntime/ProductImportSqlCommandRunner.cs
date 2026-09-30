using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

public sealed class ProductImportSqlCommandRunner(ProductRuntimeOptions options, IServiceScopeFactory scopes,
    ProductImportCommandGateway gateway, IGroupSharedMasterAuthorizationService authority,
    ICorrelationContext correlation, IHttpContextAccessor http, IProductCommandAttemptObserver observer,
    ILogger<ProductImportSqlCommandRunner> logger) : IProductImportCommandRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductImportResultV1> RunAsync(ProductImportPreparedCommand prepared,
        ReadOnlyMemory<byte> workbook, CancellationToken cancellationToken)
    {
        var request = http.HttpContext ?? throw new InvalidOperationException("Product imports require bound request metadata.");
        var keys = request.Request.Headers[ProductApiV1.IdempotencyHeader];
        if (!correlation.IsResolved || keys.Count != 1 || string.IsNullOrEmpty(keys[0])
            || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keys[0]!))) != prepared.Intent.KeyHash)
            throw new InvalidOperationException("Product import request metadata does not match its frozen intent.");
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
            try { return await ExecuteAttemptAsync(current, workbook, key, cancellationToken); }
            catch (RetryableProductImportAttemptException)
            {
                // The whole borrowed transaction was confirmed rolled back. Resolve an exact-key winner before retrying.
            }
            catch (ProductOperationException exception) when (exception.StatusCode is 404 or 409)
            {
                _ = await ReauthorizeAsync(current.Context, cancellationToken);
                throw;
            }
        }
    }

    private async Task<ProductImportResultV1> ExecuteAttemptAsync(ProductImportPreparedCommand current,
        ReadOnlyMemory<byte> workbook, string key, CancellationToken cancellationToken)
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
            await using var product = await ProductImportParticipant.EnlistAsync(connection, transaction,
                current.Context, cancellationToken);
            await using var audit = await GroupSharedAuditSession.EnlistAsync(
                scope.ServiceProvider.GetRequiredService<AuditDbContext>(), connection, transaction,
                current.Context, cancellationToken);
            await using var terminal = await GroupSharedCommandTerminalSession.EnlistAsync(connection, transaction,
                current.Context, current.Intent.KeyHash, current.Intent.RequestHash, cancellationToken);

            var write = await product.ExecuteAsync(workbook, current.Intent.Binding, cancellationToken);
            productFlushed = true;
            await observer.ObserveAsync(ProductCommandAttemptStage.ProductFlushed, current.Context, cancellationToken);
            var result = new ProductImportResultV1(write.Products, false, correlation.CorrelationId);
            audit.EnqueueBatch(write.AuditFacts.Select(fact => new GroupSharedAuditEventDraft(current.Context,
                fact.ProductId, fact.EventType, null, fact.VersionAfter, fact.BeforeJson, fact.AfterJson, fact.Reason,
                correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, correlation.CorrelationId,
                correlation.CausationId, key, AuditAiInvolvement.None, null)).ToArray());
            if (audit.IdempotencyKeyHash != current.Intent.KeyHash
                || !audit.ProductIds.SequenceEqual(write.Products.Select(value => value.ProductId)))
                throw new InvalidOperationException("The canonical audit batch does not match the Product import result and intent.");

            terminal.Stage(GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(current.Context,
                current.Intent.KeyHash, current.Intent.RequestHash), StatusCodes.Status201Created,
                JsonSerializer.Serialize(result, Json), correlation.CorrelationId, 1));
            var fresh = await ReauthorizeAsync(current.Context, cancellationToken);
            await audit.FlushAsync(fresh, cancellationToken);
            await observer.ObserveAsync(ProductCommandAttemptStage.AuditFlushed, current.Context, cancellationToken);
            await terminal.FlushAsync(fresh, cancellationToken);
            await observer.ObserveAsync(ProductCommandAttemptStage.TerminalFlushed, current.Context, cancellationToken);
            await observer.ObserveAsync(ProductCommandAttemptStage.BeforeCommit, current.Context, cancellationToken);
            _ = await ReauthorizeAsync(current.Context, cancellationToken);
            if (!productFlushed || !audit.IsFlushed || !terminal.IsFlushed)
                throw new InvalidOperationException("Product, canonical audits and terminal must flush before import commit.");
            committing = true;
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return result;
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
                    logger.LogWarning("Product import rollback could not confirm completion. CorrelationId={CorrelationId}",
                        correlation.CorrelationId);
                }
            }
            if (!rollbackConfirmed) throw new ProductImportTransactionRecoveryException(exception);
            if (!CanRetryAfterRollback(rollbackConfirmed, committing, cancellationToken.IsCancellationRequested)) throw;
            var retry = exception is GroupSharedCommandTerminalAlreadyExistsException
                || !productFlushed && exception is DbUpdateConcurrencyException concurrency
                    && concurrency.Entries.Count > 0
                    && concurrency.Entries.All(entry => entry.Entity is ProductCodeCounter)
                || !productFlushed && exception is DbUpdateException update
                    && update.InnerException is SqlException { Number: 2601 or 2627 }
                    && update.Entries.Count > 0
                    && update.Entries.All(entry => entry.Entity is ProductCodeCounter or ProductCodeRegistration or ProductRow);
            if (!retry) throw;
            throw new RetryableProductImportAttemptException(exception);
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
            throw new UnauthorizedAccessException("Product import authority changed before commit.");
        return current;
    }

    private sealed class RetryableProductImportAttemptException(Exception inner)
        : Exception("The fully rolled-back Product import attempt may be freshly resolved.", inner);
    private sealed class ProductImportTransactionRecoveryException(Exception inner)
        : Exception("Product import transaction completion could not be confirmed.", inner);
}
