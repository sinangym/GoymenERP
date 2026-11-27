using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsCustomerReturnSqlServerRelationalAcceptanceTests
{
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
}
