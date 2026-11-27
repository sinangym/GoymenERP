using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Data.SqlClient;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsCustomerReturnSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Customer_return_weighing_Down_guard_allows_unweighed_history_and_refuses_before_removing_actual_weighing_or_its_receipt()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(2, piece: true);
        await using var frame = run.DecisionFrame(); var id = Guid.NewGuid();
        await frame.Service.RequestAsync(source.InstructionId, source.LineId,
            new(id, source.DeliveryId, "6", "1", "SYNTHETIC guard history", T0), ReturnMetadata("down-request"));
        var guard = Assert.IsType<SqlOperation>(new AddCustomerReturnWeighing().DownOperations[0]).Sql;
        await run.ExecuteWeighingGuardAsync(guard);
        await frame.Service.RecordWeighingAsync(id, new("1", Guid.NewGuid(), "GrossFirst", "1013.125", "1000"), ReturnMetadata("down-weighing"));
        var before = await run.CountsAsync();
        Assert.Equal(52353, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteWeighingGuardAsync(guard))).Number);
        Assert.Equal(before, await run.CountsAsync()); Assert.Equal("13.125", (await frame.Service.GetAsync(id))!.Weighing!.NetKg);
    }

    [SqlServerFact]
    public async Task Native_SQL_weighing_and_decision_rows_refuse_overwrite_clear_partial_terminal_attach_and_source_or_version_mutation()
    {
        await using var run = await Run.StartAsync(); var source = await run.CreateDeliveredSourceAsync(2, piece: true);
        foreach (var fault in new[] { "partial", "overwrite", "clear", "terminal-attach", "version-jump", "source", "decision-fields",
                     "decision-overwrite", "decision-version-jump", "empty-pending-bump" })
        {
            await using var frame = run.DecisionFrame(); var id = Guid.NewGuid();
            await frame.Service.RequestAsync(source.InstructionId, source.LineId,
                new(id, source.DeliveryId, "6", "1", "SYNTHETIC guard " + fault, T0), ReturnMetadata("guard-request-" + fault));
            if (fault is "overwrite" or "clear" or "decision-overwrite" or "decision-version-jump")
            {
                frame.User.Actor = "operator";
                await frame.Service.RecordWeighingAsync(id, new("1", Guid.NewGuid(), "GrossFirst", "1013.125", "1000"), ReturnMetadata("guard-weigh-" + fault));
            }
            if (fault == "terminal-attach")
            {
                frame.User.Actor = "checker";
                await frame.Service.RejectAsync(id, new("1"), ReturnMetadata("guard-reject-" + fault));
            }
            var before = await frame.Service.GetAsync(id); var counts = await run.CountsAsync(); var audits = await run.DecisionAuditCount();
            var row = await frame.Db.CustomerReturns.SingleAsync(item => item.CustomerReturnId == id);
            LogisticsCustomerReturnPersistenceGuardTests.Fault(row, fault);
            await Assert.ThrowsAsync<InvalidOperationException>(() => frame.Db.SaveChangesAsync());
            frame.Db.ChangeTracker.Clear();
            Assert.Equal(before, await frame.Service.GetAsync(id)); Assert.Equal(counts, await run.CountsAsync()); Assert.Equal(audits, await run.DecisionAuditCount());
            Assert.False(frame.Writer.HasPendingDrafts);
        }
    }

    private sealed partial class Run
    {
        public async Task ExecuteWeighingGuardAsync(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
    }
}
