using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesSubcontractServiceInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Invoice_claim_range_index_can_roll_back_with_recorded_invoices_without_deleting_source_data()
    {
        await using var run = await Run.StartAsync();
        var saved = await run.StoreAsync("claim-index-rollback", Weighing);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, id => id.EndsWith("_AddSubcontractInvoiceClaimRange", StringComparison.Ordinal));
        Assert.True(index > 0);
        await context.GetService<IMigrator>().MigrateAsync(applied[index - 1]);
        Assert.Equal(applied[..index], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'sales.SubcontractServiceInvoices') AND name=N'IX_SubcontractServiceInvoices_ClaimRange'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractServiceInvoices WHERE Id=@id", ("@id", saved.Invoice.Id)));
        await context.Database.MigrateAsync();
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'sales.SubcontractServiceInvoices') AND name=N'IX_SubcontractServiceInvoices_ClaimRange'"));
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Empty_invoice_claim_range_blocks_a_concurrent_create_and_same_key_can_retry_after_reader_rollback()
    {
        await using var run = await Run.StartAsync();
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (var reader = await SalesSubcontractInvoiceClaimReadSession.EnlistAsync(connection, transaction, run.Company(CompanyA)))
        {
            var empty = await reader.LockAndReadAsync(Instruction, Weighing);
            Assert.Equal(SalesSubcontractInvoiceClaimStatusV1.Available, empty.Status);
            Assert.False(empty.HasActiveInvoice);
            Assert.Empty(empty.ActiveInvoiceIds!);
            Assert.Equal(64, empty.SourceVersion!.Length);
            await using var writer = run.SalesStore(CompanyA);
            // A real SQL command timeout proves the conflicting insert cannot pass the held empty range.
            writer.SetCommandTimeout(1);
            var blocked = await Assert.ThrowsAsync<DbUpdateException>(() => writer.Store.StoreSubcontractServiceInvoiceAsync(
                run.Envelope("claim-blocked", Preparer, SalesPermissionManifestV1.InvoicesWrite), Hash('c'),
                number => Invoice(number, Weighing, TimeProvider.System.GetUtcNow())));
            Assert.Equal(-2, Assert.IsType<SqlException>(blocked.InnerException).Number);
            Assert.Equal(empty.SourceVersion, (await reader.LockAndReadAsync(Instruction, Weighing)).SourceVersion);
        }
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Same(connection, transaction.Connection);
        await transaction.RollbackAsync();
        var retry = await run.StoreAsync("claim-blocked", Weighing);
        Assert.Equal(SalesDraftWriteOutcome.Recorded, retry.Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractServiceInvoiceAttempts"));
    }

    [SqlServerFact]
    public async Task Invoice_claim_reader_rejects_foreign_context_nonserializable_or_ended_transactions_without_owning_handles()
    {
        await using var run = await Run.StartAsync();
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using (var readCommitted = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => SalesSubcontractInvoiceClaimReadSession.EnlistAsync(
                connection, readCommitted, run.Company(CompanyA)));
            await readCommitted.RollbackAsync();
        }
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var company = new MutableClaimCompany(CompanyA);
        await using var reader = await SalesSubcontractInvoiceClaimReadSession.EnlistAsync(connection, transaction, company);
        var absent = await reader.LockAndReadAsync(Instruction, Weighing);
        Assert.False(absent.HasActiveInvoice);
        company.CompanyId = CompanyB;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.LockAndReadAsync(Instruction, Weighing));
        company.CompanyId = CompanyA;
        await transaction.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.LockAndReadAsync(Instruction, Weighing));
        Assert.Equal(ConnectionState.Open, connection.State);
    }

    private sealed class MutableClaimCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
}
