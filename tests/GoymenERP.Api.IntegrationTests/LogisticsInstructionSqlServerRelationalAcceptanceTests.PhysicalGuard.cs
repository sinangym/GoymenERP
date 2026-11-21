using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_physical_guard_loser_rolls_back_without_a_terminal_key_then_the_same_shipment_key_can_succeed_and_replay()
    {
        await using var run = await Run.StartAsync();
        var fixture = await SeedHostedShipmentAsync(run, "SYN-LOG-PHYSICAL-GUARD");
        var grant = await GrantRealizationAsync(run, $"SYN-LOG-guard-{Guid.NewGuid():N}");
        await using var host = HostedShipmentHost(run, fixture, new HostedAllocationReads(fixture), grant.Subject);
        using var client = HostedClientFor(host, grant.Subject, CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options;
        long guardVersion;
        await using (var inventory = new InventoryDbContext(options, company))
        {
            // Initialize the actual owner guard, without creating any economic stock event.
            await using (var transaction = await inventory.Database.BeginTransactionAsync())
            {
                await InventoryNegativeStockValidationGuard.AcquireAsync(inventory, CompanyA, CancellationToken.None);
                await transaction.CommitAsync();
            }
            guardVersion = await inventory.NegativeStockValidationGuards.AsNoTracking().Select(row => row.Version).SingleAsync();
        }
        var first = CreateHostedShipmentRequest(fixture, "1", "physical-guard");
        var before = await HostedEffectsAsync(run, fixture, first);
        var logisticsOutboxBefore = await run.ScalarAsync<int>(OutboxCount);
        using (var loser = new ShipmentPhysicalGuardSqlLoser(run.Factory.DatabaseName))
        using (var response = await client.SendAsync(first.Message))
        {
            Assert.True(loser.Injected);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(LogisticsProblemCodesV1.ConcurrentChange, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }
        Assert.Equal(before, await HostedEffectsAsync(run, fixture, first));
        Assert.Equal(logisticsOutboxBefore, await run.ScalarAsync<int>(OutboxCount));
        await using (var inventory = new InventoryDbContext(options, company))
            Assert.Equal(guardVersion, await inventory.NegativeStockValidationGuards.AsNoTracking().Select(row => row.Version).SingleAsync());

        // Explicit fresh HTTP scope, same actor/key/fact: the transient refusal was not saved as the key's answer.
        var reissue = CreateHostedShipmentRequest(fixture, "1", "physical-guard", first.ShipmentId, first.MovementId,
            correlationId: first.CorrelationId);
        using (var response = await client.SendAsync(reissue.Message))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }
        var accepted = await HostedEffectsAsync(run, fixture, first);
        Assert.Equal(before with
        {
            InstructionVersion = before.InstructionVersion + 1,
            ShareCount = before.ShareCount + 1,
            ShipmentAttemptCount = before.ShipmentAttemptCount + 1,
            AllocationRemainingKg = before.AllocationRemainingKg - 2m,
            AllocationVersion = before.AllocationVersion + 1,
            BalanceQuantityKg = before.BalanceQuantityKg - 2m,
            LabelVersion = before.LabelVersion + 1,
            MovementCount = before.MovementCount + 1,
            CommandCount = before.CommandCount + 1,
            OutboxCount = before.OutboxCount + 1,
            AuditCount = before.AuditCount + 2,
        }, accepted);
        Assert.Equal(logisticsOutboxBefore + 1, await run.ScalarAsync<int>(OutboxCount));
        await using (var inventory = new InventoryDbContext(options, company))
            Assert.Equal(guardVersion + 1, await inventory.NegativeStockValidationGuards.AsNoTracking().Select(row => row.Version).SingleAsync());
        var replay = CreateHostedShipmentRequest(fixture, "1", "physical-guard", first.ShipmentId, first.MovementId,
            correlationId: $"SYN-LOG-guard-replay-{Guid.NewGuid():N}");
        using (var response = await client.SendAsync(replay.Message))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(first.CorrelationId, Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }
        Assert.Equal(accepted, await HostedEffectsAsync(run, fixture, first));
        Assert.Equal(logisticsOutboxBefore + 1, await run.ScalarAsync<int>(OutboxCount));
    }

    // As in the actual Inventory guard fixture, change only the optimistic original version.
    // The real SQL UPDATE affects zero rows; EF raises the real concurrency exception.
    private sealed class ShipmentPhysicalGuardSqlLoser :
        IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string databaseName;
        private readonly ConcurrentBag<IDisposable> subscriptions = [];
        private readonly IDisposable allListeners;
        private int injected;

        public ShipmentPhysicalGuardSqlLoser(string databaseName)
        {
            this.databaseName = databaseName;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public bool Injected => Volatile.Read(ref injected) != 0;

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore") subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != CoreEventId.SaveChangesStarting.Name || value.Value is not DbContextEventData data
                || data.Context is not InventoryDbContext context || context.Database.GetDbConnection().Database != databaseName) return;
            var guard = context.ChangeTracker.Entries().SingleOrDefault(entry => entry.Metadata.GetTableName() == "NegativeStockValidationGuards");
            if (guard is null || guard.State != EntityState.Modified || Interlocked.CompareExchange(ref injected, 1, 0) != 0) return;
            guard.Property("Version").OriginalValue = checked((long)guard.Property("Version").CurrentValue! + 1L);
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            allListeners.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
