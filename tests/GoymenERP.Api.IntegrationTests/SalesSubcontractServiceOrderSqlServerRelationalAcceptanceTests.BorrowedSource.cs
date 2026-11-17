using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesSubcontractServiceOrderSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Borrowed_subcontract_order_source_reads_actual_uncommitted_versions_and_retains_caller_handles()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.StoredDraftAsync(CompanyA, "borrowed-source");
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var reader = await SalesSubcontractServiceOrderReadSession.EnlistAsync(connection, transaction, run.Company(CompanyA)))
        {
            var initial = await reader.GetAsync(CompanyA, draft.Id);
            Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.Found, initial.Outcome);
            Assert.Equal(draft.Version, initial.Order!.Version);
            Assert.Equal(draft.Header.PartyId, initial.Order.PartyId);
            Assert.Equal(Product, Assert.Single(initial.Order.Lines).ProductId);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE sales.Drafts SET Version=Version+1 WHERE CompanyId=@company AND Id=@id";
            command.Parameters.AddWithValue("@company", CompanyA); command.Parameters.AddWithValue("@id", draft.Id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            Assert.Equal(draft.Version + 1, (await reader.GetAsync(CompanyA, draft.Id)).Order!.Version);
            Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.Unreadable, (await reader.GetAsync(CompanyB, draft.Id)).Outcome);
            Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.NotFound, (await reader.GetAsync(CompanyA, Guid.NewGuid())).Outcome);
        }
        Assert.Equal(ConnectionState.Open, connection.State); Assert.Same(connection, transaction.Connection);
        await transaction.RollbackAsync();
        await using var ordinary = run.SalesStore(CompanyA);
        Assert.Equal(draft.Version, (await ordinary.Store.FindDraftAsync(draft.Id))!.Version);
    }

    [SqlServerFact]
    public async Task Borrowed_subcontract_order_source_holds_the_actual_order_and_refuses_scope_or_transaction_loss()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.StoredDraftAsync(CompanyA, "borrowed-fence");
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using (var weak = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => SalesSubcontractServiceOrderReadSession.EnlistAsync(
                connection, weak, run.Company(CompanyA)));
            await weak.RollbackAsync();
        }
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var company = new MutableSourceCompany(CompanyA);
        await using var reader = await SalesSubcontractServiceOrderReadSession.EnlistAsync(connection, transaction, company);
        Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.Found, (await reader.GetAsync(CompanyA, draft.Id)).Outcome);
        await using (var writer = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await writer.OpenAsync(); await using var command = writer.CreateCommand(); command.CommandTimeout = 1;
            command.CommandText = "UPDATE sales.Drafts SET Version=Version+1 WHERE CompanyId=@company AND Id=@id";
            command.Parameters.AddWithValue("@company", CompanyA); command.Parameters.AddWithValue("@id", draft.Id);
            Assert.Equal(-2, (await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync())).Number);
        }
        company.CompanyId = CompanyB;
        Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.Unreadable, (await reader.GetAsync(CompanyA, draft.Id)).Outcome);
        company.CompanyId = CompanyA;
        await transaction.RollbackAsync();
        Assert.Equal(SalesSubcontractServiceOrderOutcomeV1.Unreadable, (await reader.GetAsync(CompanyA, draft.Id)).Outcome);
        Assert.Equal(ConnectionState.Open, connection.State);
        await reader.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.GetAsync(CompanyA, draft.Id));
    }

    private sealed class MutableSourceCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
}
