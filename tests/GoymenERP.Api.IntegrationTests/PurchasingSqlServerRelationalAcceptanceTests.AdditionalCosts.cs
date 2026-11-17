using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
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
    public async Task Additional_cost_source_conflict_replays_its_own_terminal_fact_without_a_monetary_or_stock_effect()
    {
        await using var run = await Run.StartAsync(); var receipt = await InvoiceRateReceiptAsync(run);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        await using var scope = AdditionalCostScope(run, CompanyA); var id = Guid.NewGuid();
        var envelope = AdditionalCostEnvelope(run, "SENTETIK-expense-conflict");
        var answer = await scope.Store.RecordAsync(id, envelope, Hash("SENTETIK-expense-conflict"),
            _ => throw new PurchaseAdditionalCostException("purchasing_additional_cost_source_version_conflict"), _ => Task.CompletedTask, default);
        Assert.Equal(409, answer.Refusal!.Status); Assert.Null(answer.Cost);
        var replay = await scope.Store.ReplayAsync(envelope with { CorrelationId = "SENTETIK-later" }, Hash("SENTETIK-expense-conflict"), default);
        Assert.True(replay!.IsReplay); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(answer.Refusal.Code, replay.Refusal!.Code);
        Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, (await Assert.ThrowsAsync<PurchasingStoreException>(() =>
            scope.Store.ReplayAsync(envelope, Hash("SENTETIK-changed-expense"), default))).Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCosts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCostAttempts"));
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
    }
    [SqlServerFact]
    public async Task Additional_cost_records_actual_order_and_receipt_Audit_and_replay_without_any_extra_Inventory_movement()
    {
        await using var run = await Run.StartAsync(); var receipt = await InvoiceRateReceiptAsync(run);
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        await using var scope = AdditionalCostScope(run, CompanyA);
        var source = await scope.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, receipt.AcceptanceId, default);
        Assert.NotNull(source); var value = AdditionalCostFact(run, source!);
        var envelope = AdditionalCostEnvelope(run, "SENTETIK-extra-cost");
        var saved = await scope.Store.RecordAsync(value.CostId, envelope, Hash("SENTETIK-extra-cost"), _ => Task.FromResult(value), _ => Task.CompletedTask, default);
        Assert.Equal(value.CostId, saved.Cost!.CostId);
        var replay = await scope.Store.ReplayAsync(envelope with { CorrelationId = "SENTETIK-retry" }, Hash("SENTETIK-extra-cost"), default);
        Assert.True(replay!.IsReplay); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(value.NetAmount, replay.Cost!.NetAmount); Assert.Null(replay.Cost.ExpenseType);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCosts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.AdditionalCost'"));
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        await using var foreign = AdditionalCostScope(run, CompanyB);
        Assert.Empty(await foreign.Store.ListAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, default));
        Assert.Null(await foreign.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, null, default));
        Assert.Null(await foreign.Store.ReplayAsync(envelope, Hash("SENTETIK-extra-cost"), default));
    }
    [SqlServerFact]
    public async Task Additional_cost_late_authority_loss_and_Audit_failure_roll_back_the_fact_attempt_and_actual_Audit_append()
    {
        await using var run = await Run.StartAsync(); var receipt = await InvoiceRateReceiptAsync(run);
        await using (var scope = AdditionalCostScope(run, CompanyA))
        {
            var source = await scope.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, null, default);
            var value = AdditionalCostFact(run, source!);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Store.RecordAsync(value.CostId, AdditionalCostEnvelope(run, "SENTETIK-extra-permission-loss"),
                Hash("SENTETIK-extra-permission-loss"), _ => Task.FromResult(value), async token =>
                {
                    if (await scope.Database.Set<PurchaseAdditionalCostRow>().AsNoTracking().AnyAsync(x => x.CostId == value.CostId, token))
                        throw new UnauthorizedAccessException("SENTETIK commit öncesi yetki kaybı");
                }, default));
        }
        await using (var scope = AdditionalCostScope(run, CompanyA, failAudit: true))
        {
            var source = await scope.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, null, default);
            var value = AdditionalCostFact(run, source!);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.RecordAsync(value.CostId, AdditionalCostEnvelope(run, "SENTETIK-extra-audit-fail"),
                Hash("SENTETIK-extra-audit-fail"), _ => Task.FromResult(value), _ => Task.CompletedTask, default));
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCosts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCostAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.AdditionalCost'"));
    }
    [SqlServerFact]
    public async Task Additional_cost_composite_receipt_source_rejects_a_real_receipt_from_another_order_and_Down_51998_preserves_history()
    {
        await using var run = await Run.StartAsync(); var receipt = await InvoiceRateReceiptAsync(run);
        var other = await InvoiceRateReceiptAsync(run);
        await using var scope = AdditionalCostScope(run, CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseAdditionalCosts().DownOperations[0]).Sql;
        await scope.Database.Database.ExecuteSqlRawAsync(guard);
        var source = await scope.Store.ReadSourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, receipt.OrderId, receipt.AcceptanceId, default);
        var invalid = AdditionalCostFact(run, source!) with { OrderId = other.OrderId };
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => scope.Store.RecordAsync(invalid.CostId, AdditionalCostEnvelope(run, "SENTETIK-extra-wrong-receipt"),
            Hash("SENTETIK-extra-wrong-receipt"), _ => Task.FromResult(invalid), _ => Task.CompletedTask, default));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCosts"));
        var valid = AdditionalCostFact(run, source!);
        await scope.Store.RecordAsync(valid.CostId, AdditionalCostEnvelope(run, "SENTETIK-extra-down"), Hash("SENTETIK-extra-down"),
            _ => Task.FromResult(valid), _ => Task.CompletedTask, default);
        Assert.Equal(51998, (await Assert.ThrowsAsync<SqlException>(() => scope.Database.Database.ExecuteSqlRawAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.AdditionalCosts"));
    }
    private static PurchasingWriteEnvelope AdditionalCostEnvelope(Run run, string key) =>
        run.Envelope(key) with { PermissionCode = PurchasingPermissionManifestV1.OrdersRevise };
    // Store persistence/transaction proof only. Catalog, Cari and recording authority here are synthetic;
    // the actual company-filtered order/acceptance, Audit append and Inventory non-effect are SQL-owned.
    private static PurchaseAdditionalCostState AdditionalCostFact(Run run, PurchaseAdditionalCostSourceV1 source) =>
        PurchaseAdditionalCost.Record(new(source.CompanyId, Guid.NewGuid(), source.OrderId, source.OrderVersion, source.OrderNumber,
            source.Kind, source.AcceptanceId, source.AcceptanceLineId, Guid.NewGuid(), Guid.NewGuid(), "1", "1", Day, source.Money,
            "SENTETIK nakliye", null, new string('c', 64), 100.01m, new("SENTETIK-20", 1, Day, 20m), 20m, 120.01m,
            run.Actor, "SENTETIK masraf belgesi", DateTimeOffset.UtcNow));
    private static AdditionalCostSqlScope AdditionalCostScope(Run run, Guid id, bool failAudit = false)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ICompanyContext>().Set(id); var database = services.GetRequiredService<PurchasingDbContext>();
        var writer = services.GetRequiredService<IAuditTransactionWriterV1>();
        return new(scope, database, new(database, services.GetRequiredService<ICompanyContext>(), services.GetRequiredService<IPurchasingStore>(),
            services.GetRequiredService<IPurchaseAcceptanceSourceV1>(), services.GetRequiredService<IPurchaseServiceOrderStoreV1>(),
            services.GetRequiredService<IAuditEventCollector>(), failAudit ? new ServiceFailingAudit(writer) : writer));
    }
    private sealed class AdditionalCostSqlScope(AsyncServiceScope scope, PurchasingDbContext database, PurchaseAdditionalCostStoreV1 store) : IAsyncDisposable
    {
        public PurchasingDbContext Database => database; public PurchaseAdditionalCostStoreV1 Store => store;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
