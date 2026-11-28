using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Exact_access_approval_audit_reader_borrows_actual_caller_transaction_without_owning_handles_or_pending_work()
    {
        var actor = new PurposeActorSlot();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
            using (client)
            {
                actor.Id = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
                await using var scope = factory.Services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var service = Assert.IsAssignableFrom<IIdentityAccessGrantDecisionV1>(scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
                var metadata = PurposeMetadata(actor.Id.Value, NewKey(), NewKey());
                var preview = await service.ReadDecisionAsync(pending.Id, metadata, _ => Task.CompletedTask);
                Assert.NotNull(preview);
                var result = await service.DecidePurposeAsync(pending.Id, true, "1", null, preview.CurrentImpactDigest, metadata, _ => Task.CompletedTask);
                Assert.Equal(200, result.Answer.StatusCode);
                Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));

                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                db.ChangeTracker.Clear();
                await using var caller = await db.Database.BeginTransactionAsync();
                var providerTransaction = caller.GetDbTransaction();
                var connection = providerTransaction.Connection;
                Assert.NotNull(connection);
                var callerUser = new UserAccount(Guid.NewGuid(), "SENTETIK borrowed caller work", null);
                db.Users.Add(callerUser);
                var reader = scope.ServiceProvider.GetRequiredService<IIdentityAccessDecisionAuditReadV1>();
                var selector = new IdentityAccessDecisionAuditSelectorV1(TestCompanyIds.CompanyA, pending.Id,
                    metadata.ActorId, metadata.ActorId, metadata.CorrelationId, IdempotencyHash.ComputeKey(metadata.IdempotencyKey));

                var artifact = await reader.ReadAsync(selector, _ => Task.CompletedTask, providerTransaction);
                Assert.NotNull(artifact);
                Assert.Equal(1L, artifact.VersionBefore); Assert.Equal(2L, artifact.VersionAfter);
                Assert.Contains("appliedPermissionDigest", artifact.AfterJson, StringComparison.Ordinal);
                AssertCallerStillOwnsEverything();

                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(
                    selector with { CurrentActorId = Guid.NewGuid().ToString("D") }, _ => Task.CompletedTask, providerTransaction));
                AssertCallerStillOwnsEverything();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(
                    selector with { CompanyId = TestCompanyIds.CompanyB }, _ => Task.CompletedTask, providerTransaction));
                AssertCallerStillOwnsEverything();

                using var cancelled = new CancellationTokenSource();
                var callbacks = 0;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(selector, _ =>
                {
                    // Entry, after borrowed UseTransactionAsync, then after the real Audit query.
                    if (++callbacks == 3) cancelled.Cancel();
                    return Task.CompletedTask;
                }, providerTransaction, cancelled.Token));
                Assert.Equal(3, callbacks);
                AssertCallerStillOwnsEverything();

                var failure = new InvalidOperationException("SENTETIK consumer failure; caller retains ownership");
                var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(selector,
                    _ => throw failure, providerTransaction));
                Assert.Same(failure, thrown);
                AssertCallerStillOwnsEverything();

                // A second successful read proves that failed/cancelled borrowed reads did not complete the caller TX.
                var again = await reader.ReadAsync(selector, _ => Task.CompletedTask, providerTransaction);
                Assert.Equal(artifact, again);
                AssertCallerStillOwnsEverything();
                await caller.RollbackAsync(); // Only this caller owns rollback/disposal.
                await caller.DisposeAsync();
                db.ChangeTracker.Clear();
                Assert.False(await db.Users.AsNoTracking().AnyAsync(x => x.Id == callerUser.Id));
                Assert.Single(await db.CommandExecutions.Where(x => x.OriginalCorrelationId == metadata.CorrelationId).ToListAsync());
                Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents
                    .CountAsync(x => x.CorrelationId == metadata.CorrelationId));
                actor.Id = null;

                void AssertCallerStillOwnsEverything()
                {
                    Assert.Same(caller, db.Database.CurrentTransaction);
                    Assert.Same(connection, providerTransaction.Connection);
                    Assert.Equal(ConnectionState.Open, connection.State);
                    Assert.Equal(EntityState.Added, db.Entry(callerUser).State);
                    Assert.True(db.ChangeTracker.HasChanges());
                }
            }
        }, services => ConfigurePurposeActor(services, actor));
    }
}
