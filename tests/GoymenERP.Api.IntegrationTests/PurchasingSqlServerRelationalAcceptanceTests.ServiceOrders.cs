using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Service_order_number_snapshot_acceptance_and_Audit_share_one_transaction_and_replay_original_facts()
    {
        await using var run = await Run.StartAsync();
        var draft = ServiceDraft(run); var key = run.Envelope("service-create");
        await using (var scope = ServiceScope(run, CompanyA))
        {
            var created = await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, key, Hash("service-create"), "SENTETIK hizmet",
                (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
            Assert.NotNull(created!.Order!.DocumentNumber);
            var replay = await scope.Store.ReplayAsync(PurchaseServiceOrderCommandsV1.Create, key with { CorrelationId = "SENTETIK-retry" }, Hash("service-create"), default);
            Assert.True(replay!.IsReplay); Assert.Equal(key.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(created.Order.DocumentNumber, replay.Order!.DocumentNumber);
            await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, run.Envelope("service-approve"), Hash("service-approve"), "SENTETIK onay",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, "SENTETIK-checker", old!.CreatedAtUtc)), _ => Task.CompletedTask, default);
            var acceptance = Guid.NewGuid();
            var accepted = await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Accept, run.Envelope("service-accept"), Hash("service-accept"), "SENTETIK kabul",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Accept(old!, 2, acceptance, draft.Lines[0].LineId, 4m, Day,
                    run.Actor, "SENTETIK fiili hizmet", old!.CreatedAtUtc)), _ => Task.CompletedTask, default);
            var source = await ((IPurchaseServiceAcceptanceReadPortV1)scope.Store).GetAsync(CompanyA, draft.OrderId, acceptance);
            Assert.Equal(PurchaseServiceAcceptanceOutcomeV1.Available, source.Outcome); Assert.Equal("4", source.Source!.Quantity);
            Assert.Equal(accepted!.Order!.Version.ToString(), source.Source.OrderVersion);
            Assert.Equal(PurchaseSubcontractOrderOutcomeV1.NotSubcontract,
                (await ((IPurchaseSubcontractOrderReadPortV1)scope.Store).GetAsync(CompanyA, draft.OrderId)).Outcome);
        }
        await using (var foreign = ServiceScope(run, CompanyB))
        {
            Assert.Null(await foreign.Store.ReadAsync(draft.OrderId, default));
            Assert.Equal(PurchaseSubcontractOrderOutcomeV1.Unavailable,
                (await ((IPurchaseSubcontractOrderReadPortV1)foreign.Store).GetAsync(CompanyA, draft.OrderId)).Outcome);
        }
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderRevisions"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.ServiceOrder'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
    }

    [SqlServerFact]
    public async Task Service_order_Audit_failure_after_real_append_rolls_back_number_revision_attempt_and_Audit()
    {
        await using var run = await Run.StartAsync(); var draft = ServiceDraft(run);
        var beforeNumbers = await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberReservations");
        await using (var scope = ServiceScope(run, CompanyA, failAudit: true))
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create,
                run.Envelope("service-audit-fail"), Hash("service-audit-fail"), "SENTETIK rollback",
                (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrders"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.ServiceOrder'"));
        Assert.Equal(beforeNumbers, await run.ScalarAsync<int>("SELECT COUNT(*) FROM settings.DocumentNumberReservations"));
    }

    [SqlServerFact]
    public async Task Service_order_terminal_version_refusal_replays_without_mutating_the_saved_revision()
    {
        await using var run = await Run.StartAsync(); var draft = ServiceDraft(run);
        await using var scope = ServiceScope(run, CompanyA);
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("service-version-create"), Hash("service-version-create"), "SENTETIK kayıt",
            (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        var envelope = run.Envelope("service-stale");
        var answer = await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, envelope, Hash("service-stale"), "SENTETIK bayat sürüm",
            (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 9, "SENTETIK-checker", old!.CreatedAtUtc)), _ => Task.CompletedTask, default);
        Assert.Equal(PurchaseServiceOrder.VersionConflict, answer!.Refusal!.Code);
        Assert.True((await scope.Store.ReplayAsync(PurchaseServiceOrderCommandsV1.Approve, envelope, Hash("service-stale"), default))!.IsReplay);
        Assert.Equal(1, (await scope.Store.ReadAsync(draft.OrderId, default))!.Version);
    }

    [SqlServerFact]
    public async Task Service_order_refuses_borrowed_state_before_cleanup_and_Down_51992_preserves_recorded_history()
    {
        await using var run = await Run.StartAsync(); var draft = ServiceDraft(run);
        await using var scope = ServiceScope(run, CompanyA);
        await using (var borrowed = await scope.Database.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create,
                run.Envelope("service-borrowed"), Hash("service-borrowed"), "SENTETIK çağıran işlem",
                (_, _) => throw new InvalidOperationException("Mutate must not run"), _ => Task.CompletedTask, default));
            Assert.Same(borrowed, scope.Database.Database.CurrentTransaction); await borrowed.CommitAsync();
        }
        await scope.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("service-down"), Hash("service-down"), "SENTETIK Down kanıtı",
            (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        var guard = new AddPurchaseServiceOrders().DownOperations.OfType<SqlOperation>().First().Sql;
        var error = await Assert.ThrowsAsync<SqlException>(() => scope.Database.Database.ExecuteSqlRawAsync(guard));
        Assert.Equal(51992, error.Number); Assert.NotNull(await scope.Store.ReadAsync(draft.OrderId, default));
    }

    private static PurchaseServiceOrderState ServiceDraft(Run run) => PurchaseServiceOrder.Create(new(CompanyA, Guid.NewGuid(), 1,
        PurchaseServiceOrderKind.ServicePurchase, PurchaseServiceOrderStatus.Draft, Guid.NewGuid(), Guid.NewGuid(), "1", "1", Day,
        run.Actor, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4),
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK hizmet", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 10m, 2m, 0m)], [], [], []));
    private static ServiceSqlScope ServiceScope(Run run, Guid company, bool failAudit = false)
    {
        var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var database = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var numbers = new PurchaseDocumentNumberSqlTestOwner(run.Factory.Services, company,
            new PurchaseDocumentNumberSqlTestOwner.FixedActor(company, run.Actor), new PurchaseDocumentNumberSqlTestOwner.DirectStoreAuthority());
        return new(scope, database, numbers, new(database, scope.ServiceProvider.GetRequiredService<ICompanyContext>(),
            scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), failAudit ? new ServiceFailingAudit(writer) : writer, numbers.Numbers));
    }
    private sealed class ServiceSqlScope(AsyncServiceScope scope, PurchasingDbContext database, PurchaseDocumentNumberSqlTestOwner numbers,
        PurchaseServiceOrderStoreV1 store) : IAsyncDisposable
    {
        public PurchaseServiceOrderStoreV1 Store => store; public PurchasingDbContext Database => database;
        public async ValueTask DisposeAsync() { await numbers.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class ServiceFailingAudit(IAuditTransactionWriterV1 inner) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default)
        { await inner.WritePendingAsync(transaction, token); throw new InvalidOperationException("SENTETIK Audit sonrası hata"); }
        public void DiscardPending() => inner.DiscardPending();
    }
}
