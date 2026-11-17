using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_non_kg_Down_guard_allows_an_empty_history_and_preserves_a_real_measurement_before_any_drop()
    {
        await using var run = await Run.StartAsync(); var (_, order) = await run.ApprovedOrderAsync(CompanyA);
        var guard = Assert.IsType<SqlOperation>(new AddManufacturingNonKgInputFacts().DownOperations[0]).Sql;
        Assert.Equal(1, await run.ScalarAsync<int>(guard + "; SELECT 1"));
        await using (var scope = run.Store(CompanyA)) await scope.Corrections.RecordNonKgInputAsync(order, 1, 0,
            new(Guid.NewGuid(), 1, ManufacturingNonKgInputKindV1.Paint, "Synthetic measured paint", ManufacturingNonKgInputUnitV1.Litre,
                "2.25", At, null, "Synthetic actual measurement"), Metadata(run));
        Assert.Equal(51963, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.NonKgInputFacts"));
    }
    [SqlServerFact]
    public async Task Actual_non_kg_facts_keep_exact_quantities_correction_history_and_audit_in_the_same_transaction()
    {
        await using var run = await Run.StartAsync(); var (_, order) = await run.ApprovedOrderAsync(CompanyA);
        var first = new ManufacturingNonKgInputConsumptionV1(Guid.NewGuid(), 1, ManufacturingNonKgInputKindV1.Paint,
            "Synthetic real paint", ManufacturingNonKgInputUnitV1.Litre, "2.2500000000000000000000000001", At, null, "Synthetic actual measurement");
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Corrections.RecordNonKgInputAsync(order, 1, 0, first, Metadata(run));
            await scope.Corrections.RecordNonKgInputAsync(order, 1, 1, first with { FactId = Guid.NewGuid(), Revision = 2,
                Quantity = "0", ReplacesFactId = first.FactId, Reason = "Synthetic correction" }, Metadata(run));
        }
        await using (var fresh = run.Store(CompanyA))
        {
            var facts = (await fresh.Corrections.ReadNonKgInputsAsync(order))!;
            Assert.Equal(1, facts.OrderVersion); Assert.Equal(2, facts.Revision); Assert.Equal(first, facts.Facts[0]);
            Assert.Equal(first.FactId, facts.Facts[1].ReplacesFactId); Assert.Equal("0", facts.Facts[1].Quantity);
        }
        await using (var foreign = run.Store(CompanyB)) Assert.Null(await foreign.Corrections.ReadNonKgInputsAsync(order));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.non-kg-input-recorded.v1'"));
        await using (var scope = run.Store(CompanyA))
        {
            await using var tx = await scope.Context.Database.BeginTransactionAsync();
            await scope.Corrections.RecordNonKgInputAsync(order, 1, 2, first with { FactId = Guid.NewGuid(), Revision = 3 }, Metadata(run));
            await tx.RollbackAsync();
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.NonKgInputFacts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.non-kg-input-recorded.v1'"));
    }

    [SqlServerFact]
    public async Task A_non_kg_writer_with_a_stale_open_order_cannot_commit_after_the_order_has_closed()
    {
        await using var run = await Run.StartAsync(); var (_, order) = await run.ApprovedOrderAsync(CompanyA);
        await using var stale = run.Store(CompanyA);
        _ = await stale.Context.Orders.SingleAsync(row => row.Id == order);
        await using (var closer = run.Store(CompanyA)) await closer.Orders.CloseOrderAsync(order, 1, run.Actor, At, Metadata(run));
        var fact = new ManufacturingNonKgInputConsumptionV1(Guid.NewGuid(), 1, ManufacturingNonKgInputKindV1.Other,
            "Synthetic packaging", ManufacturingNonKgInputUnitV1.Piece, "1", At, null, "Synthetic actual consumption");
        var refusal = await Assert.ThrowsAsync<ManufacturingStoreException>(() => stale.Corrections.RecordNonKgInputAsync(order, 1, 0, fact, Metadata(run)));
        Assert.Equal(ManufacturingStoreCodes.ConcurrentChange, refusal.Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.NonKgInputFacts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.order.non-kg-input-recorded.v1'"));
    }
}
