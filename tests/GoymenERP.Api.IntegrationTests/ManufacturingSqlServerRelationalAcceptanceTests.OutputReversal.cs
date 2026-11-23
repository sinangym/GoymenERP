using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-009 D2 (URT-D28, URT-D30) at the store on a REAL SQL Server: a declaration bound to its plan output line keeps the
/// line, a reversal is its own row linked to the declaration, replayed by id and refused for a declaration without a
/// line, the restored order is net of it, and the migration's Down passes while only unlined declarations exist and
/// refuses (51284) once a line reference or a reversal is stored.
/// </summary>
public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_output_reversal_migrations_Down_passes_with_unlined_outputs_and_refuses_while_a_reversal_exists()
    {
        await using var run = await Run.StartAsync();
        var (_, orderId) = await run.ApprovedOrderAsync(CompanyA);
        var unlined = Guid.NewGuid();
        var lined = Guid.NewGuid();
        var reversalId = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
        {
            await store.Orders.IssueInputAsync(orderId, 1, Guid.NewGuid(), RollA, Kg(10_000m), At, Metadata(run));
            await store.Orders.DeclareOutputAsync(orderId, 2, unlined, RollA, OutputClass.GoodProduct, Kg(1_000m),
                At.AddHours(1), Metadata(run));
        }

        var guard = Assert.IsType<SqlOperation>(new AddManufacturingOutputReversals().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + "; SELECT 1");

        // The plan has no slit or cut line: its good product binds to the class line (ASSUMPTION-URT-F009-1).
        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Recorded, await store.Corrections.DeclareOutputAsync(orderId, 3, lined,
                RollA, OutputClass.GoodProduct, Kg(500m), At.AddHours(2), PlanOutputLineReference.ClassLine,
                Metadata(run)));
        }

        var lineRefused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51284, lineRefused.Number);

        await using (var store = run.Store(CompanyA))
        {
            var missing = await Assert.ThrowsAsync<ManufacturingRuleException>(() => store.Corrections.ReverseOutputAsync(
                orderId, 4, Guid.NewGuid(), unlined, Kg(10m), "tartım düzeltmesi", At.AddHours(3), Metadata(run)));
            Assert.Equal(ManufacturingRuleCodes.OutputLineMissing, missing.Code);

            Assert.Equal(ManufacturingWriteOutcome.Recorded, await store.Corrections.ReverseOutputAsync(orderId, 4,
                reversalId, lined, Kg(100m), "tartım düzeltmesi", At.AddHours(3), Metadata(run)));
        }

        await using (var store = run.Store(CompanyA))
        {
            Assert.Equal(ManufacturingWriteOutcome.Replayed, await store.Corrections.ReverseOutputAsync(orderId, 4,
                reversalId, lined, Kg(100m), "tartım düzeltmesi", At.AddHours(3), Metadata(run)));
            var stored = (await store.Orders.FindOrderAsync(orderId))!;
            Assert.Equal(5L, stored.Version);
            Assert.Equal(400m, stored.NetMassOf(lined).Value);
            Assert.Equal(PlanOutputLineReference.ClassLine,
                stored.Outputs.Single(output => output.OutputId == lined).OutputLine);
            Assert.Null(stored.Outputs.Single(output => output.OutputId == unlined).OutputLine);
            Assert.Equal(reversalId, Assert.Single(stored.OutputReversals).ReversalId);
        }

        var reversalRefused = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "; SELECT 1"));
        Assert.Equal(51284, reversalRefused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.OutputReversals WHERE OutputId = @o", ("@o", lined)));
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT OutputLineKind FROM manufacturing.Facts WHERE Id = @o", ("@o", lined)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.output.reversed.v1'"));
    }
}
