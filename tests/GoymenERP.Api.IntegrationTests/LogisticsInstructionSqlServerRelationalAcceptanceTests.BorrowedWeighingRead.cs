using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    // Real SQL/invariant rebuild; owner facts and Audit writes are real. Sales invoice locking is a separate host fact.
    [SqlServerFact]
    public async Task Borrowed_delivery_reader_sees_the_same_uncommitted_weighing_and_never_owns_commit_or_connection()
    {
        await using var run = await Run.StartAsync();
        var company = new CompanyContext(); company.Set(CompanyA);
        var audit = new AuditEventCollector();
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var context = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        await context.Database.UseTransactionAsync(transaction);
        var store = new LogisticsStore(context, company, audit, TimeProvider.System);
        var created = await store.StoreInstructionAsync(run.Envelope("borrow-create"), Hash("borrow-create"), Day, () => Instruction(Owners.For(CompanyA)));
        var instructionId = created.Instruction.Instruction.Id;
        var lineId = Assert.Single(created.Instruction.Instruction.Lines).Id;
        await store.MutateInstructionAsync(run.Envelope("borrow-issue"), Hash("borrow-issue"), LogisticsCommands.IssueV1,
            instructionId, (value, now) => value.Issue(2, now), Issued);
        await store.MutateInstructionAsync(run.Envelope("borrow-load"), Hash("borrow-load"), LogisticsCommands.RecordLoadV1,
            instructionId, (value, now) => value.RecordLoad(3, Guid.NewGuid(), lineId, Kg(4), MeasurementKind.Measured, now), Loaded);
        var weighingId = Guid.NewGuid();
        await store.MutateInstructionAsync(run.Envelope("borrow-weigh"), Hash("borrow-weigh"), LogisticsCommands.RecordWeighingV1,
            instructionId, (value, now) => value.RecordWeighing(4, weighingId, WeighingDirection.TareFirst, Kg(100), Kg(104), run.Actor, now), Weighed);
        await using (var reader = await LogisticsSubcontractDeliveryReadSession.EnlistAsync(connection, transaction, CompanyA))
        {
            var value = await reader.GetAsync(CompanyA, instructionId, weighingId);
            Assert.Equal(LogisticsSubcontractDeliveryNetWeightStatusV1.Available, value.Status);
            Assert.Equal("4", value.Value!.NetKg); Assert.Equal(1, value.Value.WeighingRevision); Assert.Equal(5, value.Value.InstructionVersion);
            Assert.Equal(lineId, Assert.Single(value.Value.Lines).LineId);
            var foreign = await reader.GetAsync(CompanyB, instructionId, weighingId);
            Assert.Equal(LogisticsSubcontractDeliveryNetWeightStatusV1.Unavailable, foreign.Status); Assert.Null(foreign.Value);
            var missing = await reader.GetAsync(CompanyA, instructionId, Guid.NewGuid());
            Assert.Equal(LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, missing.Status);
        }
        Assert.Equal(ConnectionState.Open, connection.State); Assert.Same(connection, transaction.Connection);
        await transaction.RollbackAsync();
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id=@id", ("@id", instructionId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id", ("@id", instructionId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Borrowed_delivery_reader_rejects_missing_company_and_transaction_that_has_ended()
    {
        await using var run = await Run.StartAsync();
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => LogisticsSubcontractDeliveryReadSession.EnlistAsync(connection, transaction, Guid.Empty));
        await using var reader = await LogisticsSubcontractDeliveryReadSession.EnlistAsync(connection, transaction, CompanyA);
        await transaction.RollbackAsync();
        var unavailable = await reader.GetAsync(CompanyA, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(LogisticsSubcontractDeliveryNetWeightStatusV1.Unavailable, unavailable.Status);
        Assert.Null(unavailable.Value); Assert.Equal(ConnectionState.Open, connection.State);
    }
}
