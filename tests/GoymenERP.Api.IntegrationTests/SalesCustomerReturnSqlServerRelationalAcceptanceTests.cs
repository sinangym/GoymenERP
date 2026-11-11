using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-40 (EK57/EK58 SAT-D288…D294) against a REAL SQL Server and the REAL host composition: the REAL customer return service over
/// the real Sales store records the customer's return invoice against a finalised USD invoice of company A in one save (return,
/// attempt, audit), replays the same key, refuses the preparer's own approval as the key's recorded 409, is approved by another
/// actor and finalised only through the Finance reversal port (without it: 503 and nothing written, SAT-D294). A second return takes
/// the rest of the line (finalised priors only, SAT-D292 (6)), a third is over the remaining quantity (SAT-D293 (3)). The database
/// itself refuses a second return of the same customer document, a changed document, a skipped step and a delete (TR 51255) and any
/// change of an attempt (TR 51256). Another company neither sees nor decides a return; the migration refuses to roll back over a
/// recorded return (THROW 51257, forward fix). SAT-D290 (2, 3): a pending return is rejected only by another actor, a draft or pending
/// one is cancelled, both are terminal on the database too (TR 51255, CK maker-checker) and that migration's Down refuses (THROW 51258). No stock movement and no other module's table is written. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesCustomerReturnSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 7);
    private static readonly Guid Usd = Guid.Parse("6d2e3f40-5b6a-4c7d-8e9f-0a1b2c3d4e01");
    private static readonly Guid Try = Guid.Parse("6d2e3f40-5b6a-4c7d-8e9f-0a1b2c3d4e02");
    private static readonly Guid Kg = Guid.Parse("6d2e3f40-5b6a-4c7d-8e9f-0a1b2c3d4e03");
    private static readonly Guid Product = Guid.Parse("6d2e3f40-5b6a-4c7d-8e9f-0a1b2c3d4e04");
    private const string Preparer = "SENTETIK-return-preparer";
    private const string Approver = "SENTETIK-return-approver";
    private const string Finalizer = "SENTETIK-return-finalizer";

    [SqlServerFact]
    public async Task A_customer_return_is_recorded_in_one_save_approved_by_another_actor_and_finalized_only_through_the_finance_port_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (invoice, shipment) = await run.FinalizedInvoiceAsync("a");
        var line = invoice.Lines.Single(item => item.LineNo == 1);
        var finance = new FinancePort();

        await using var scope = run.SalesStore(CompanyA);
        var preparer = run.Service(scope.Store, CompanyA, Preparer, null);
        var create = new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-1", Day,
            [new CreateSalesCustomerReturnLineRequest(line.Id, "1")], shipment);
        var created = await preparer.CreateCustomerReturnAsync(create, Metadata("k-create"));
        Assert.Equal(("Draft", "1", false, shipment), (created.CustomerReturn.State, created.CustomerReturn.Version, created.IsReplay,
            created.CustomerReturn.ShipmentId));
        var id = created.CustomerReturn.Id;
        // One save: the return, its attempt and its audit; the invoice and its lines are not touched.
        Assert.Equal((1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturns] WHERE [Id] = @id AND [State] = 1", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [CustomerReturnId] = @id AND [Outcome] = 1",
                ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.CustomerReturn' "
                + "AND [EventType] = N'sales.customer-return.created.v1' AND [ActorId] = @actor", ("@actor", Preparer))));

        // The same key replays and writes nothing again.
        var replay = await preparer.CreateCustomerReturnAsync(create, Metadata("k-create"));
        Assert.Equal((true, id), (replay.IsReplay, replay.CustomerReturn.Id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturns]"));

        await preparer.SubmitCustomerReturnAsync(id, new SalesDraftStateCommandRequest("1"), Metadata("k-submit"));

        // The preparer's own approval is refused and recorded under its key (SAT-D290); nothing moves.
        var self = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            preparer.ApproveCustomerReturnAsync(id, new SalesDraftStateCommandRequest("2"), Metadata("k-self")));
        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval), (self.Status, self.Code));
        Assert.Equal((2, 1), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [Outcome] = 2")));

        var approved = await run.Service(scope.Store, CompanyA, Approver, null)
            .ApproveCustomerReturnAsync(id, new SalesDraftStateCommandRequest("2"), Metadata("k-approve"));
        Assert.Equal(("Approved", "3", Approver), (approved.CustomerReturn.State, approved.CustomerReturn.Version,
            approved.CustomerReturn.ApprovedBy));

        // SAT-D294: without the Finance reversal port the finalisation fails closed and writes nothing.
        var closed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => run.Service(scope.Store, CompanyA, Finalizer, null)
            .FinalizeCustomerReturnAsync(id, new SalesDraftStateCommandRequest("3"), Metadata("k-final-closed")));
        Assert.Equal((503, SalesCustomerReturnProblemCodesV1.FinancePortUnavailable), (closed.Status, closed.Code));
        Assert.Equal((3, 0), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [CommandName] = N'"
                + SalesCustomerReturnCommands.FinalizeV1 + "'")));

        var finalizer = run.Service(scope.Store, CompanyA, Finalizer, finance);
        var finalized = await finalizer.FinalizeCustomerReturnAsync(id, new SalesDraftStateCommandRequest("3"), Metadata("k-final"));
        Assert.Equal(("Finalized", "4", Finalizer, 1), (finalized.CustomerReturn.State, finalized.CustomerReturn.Version,
            finalized.CustomerReturn.FinalizedBy, finance.Calls));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT [FinalizationSequence] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)));

        // A second return takes the rest of the line: the finalised first return is its prior and it is the line's last return.
        var second = await preparer.CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-2", Day,
            [new CreateSalesCustomerReturnLineRequest(line.Id, "2")], shipment), Metadata("k-create-2"));
        Assert.True(Assert.Single(second.CustomerReturn.Lines).IsLastReturn);
        // A third return is over the remaining quantity only once the second is finalised; until then the remaining is still 2.
        await preparer.SubmitCustomerReturnAsync(second.CustomerReturn.Id, new SalesDraftStateCommandRequest("1"), Metadata("k-submit-2"));
        await run.Service(scope.Store, CompanyA, Approver, null)
            .ApproveCustomerReturnAsync(second.CustomerReturn.Id, new SalesDraftStateCommandRequest("2"), Metadata("k-approve-2"));
        await finalizer.FinalizeCustomerReturnAsync(second.CustomerReturn.Id, new SalesDraftStateCommandRequest("3"), Metadata("k-final-2"));
        var over = await Assert.ThrowsAsync<SalesDraftServiceException>(() => preparer.CreateCustomerReturnAsync(
            new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-3", Day, [new CreateSalesCustomerReturnLineRequest(line.Id, "0.001")],
                shipment), Metadata("k-create-3")));
        Assert.Equal((422, SalesRuleCodes.CustomerReturnQuantityExceeded), (over.Status, over.Code));
        Assert.Equal((2, 2, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturns] WHERE [State] = 4"),
            await run.ScalarAsync<int>("SELECT MAX([FinalizationSequence]) FROM [sales].[CustomerReturns]"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturns] WHERE [CustomerDocumentNo] = N'IADE-SENTETIK-3'")));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_duplicate_customer_document_a_changed_document_a_skipped_step_a_delete_and_any_attempt_change()
    {
        await using var run = await Run.StartAsync();
        var (invoice, shipment) = await run.FinalizedInvoiceAsync("g");
        Guid id;
        await using (var scope = run.SalesStore(CompanyA))
        {
            id = (await run.Service(scope.Store, CompanyA, Preparer, null).CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(
                invoice.Id, "IADE-SENTETIK-G", Day, [new CreateSalesCustomerReturnLineRequest(invoice.Lines[0].Id, "1")], shipment),
                Metadata("k-create"))).CustomerReturn.Id;
        }

        const string where = " WHERE [Id] = @id; SELECT 1";
        // UX_CustomerReturns_CustomerDocument: one return per customer document of a party.
        Assert.Equal(2601, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(CopyOf("N'IADE-SENTETIK-G'"), ("@id", id))))
            .Number);
        // TR_CustomerReturns_Guard: the customer's document never changes, a step is never skipped and nothing is deleted.
        Assert.Equal(51255, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturns] SET [CustomerDocumentNo] = N'IADE-SENTETIK-X', [Version] = [Version] + 1" + where,
            ("@id", id)))).Number);
        Assert.Equal(51255, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturns] SET [State] = 3, [SubmittedBy] = N'SENTETIK-s', [SubmittedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[ApprovedBy] = N'SENTETIK-a', [ApprovedAtUtc] = SYSDATETIMEOFFSET(), [Version] = 3" + where, ("@id", id)))).Number);
        // FK_CustomerReturnAttempts_CustomerReturns_CompanyId_CustomerReturnId is checked before the AFTER trigger; the trigger's own
        // DELETE refusal is proven below on a row no attempt references.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[CustomerReturns]" + where, ("@id", id)))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>(CopyOf("N'IADE-SENTETIK-RAW'"), ("@id", id)));
        Assert.Equal(51255, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[CustomerReturns] WHERE [CustomerDocumentNo] = N'IADE-SENTETIK-RAW'; SELECT 1"))).Number);
        // TR_CustomerReturnAttempts_Guard: the attempts are append-only.
        Assert.Equal(51256, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturnAttempts] SET [ActorId] = N'SENTETIK-other' WHERE [CustomerReturnId] = @id; SELECT 1",
            ("@id", id)))).Number);
        Assert.Equal(51256, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[CustomerReturnAttempts] WHERE [CustomerReturnId] = @id; SELECT 1", ("@id", id)))).Number);
        Assert.Equal((1, "IADE-SENTETIK-G", 1), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)),
            await run.ScalarAsync<string>("SELECT [CustomerDocumentNo] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [CustomerReturnId] = @id", ("@id", id))));
    }

    [SqlServerFact]
    public async Task Another_company_neither_sees_nor_records_nor_decides_a_customer_return_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (invoice, shipment) = await run.FinalizedInvoiceAsync("c");
        Guid id;
        await using (var scope = run.SalesStore(CompanyA))
        {
            id = (await run.Service(scope.Store, CompanyA, Preparer, null).CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(
                invoice.Id, "IADE-SENTETIK-C", Day, [new CreateSalesCustomerReturnLineRequest(invoice.Lines[0].Id, "1")], shipment),
                Metadata("k-create"))).CustomerReturn.Id;
        }

        await using (var other = run.SalesStore(CompanyB))
        {
            var foreign = run.Service(other.Store, CompanyB, Approver, null);
            Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => foreign.GetCustomerReturnAsync(id))).Status);
            Assert.Empty((await foreign.ListCustomerReturnsAsync(null)).Items);
            Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                foreign.SubmitCustomerReturnAsync(id, new SalesDraftStateCommandRequest("1"), Metadata("k-foreign-submit")))).Status);
            var record = await Assert.ThrowsAsync<SalesDraftServiceException>(() => foreign.CreateCustomerReturnAsync(
                new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-B", Day,
                    [new CreateSalesCustomerReturnLineRequest(invoice.Lines[0].Id, "1")], shipment), Metadata("k-foreign-create")));
            Assert.Equal((422, SalesCustomerReturnProblemCodesV1.InvoiceNotFound), (record.Status, record.Code));
        }

        Assert.Equal((1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturns]"),
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts]")));
    }

    [SqlServerFact]
    public async Task The_customer_return_migrations_Down_passes_with_no_return_and_refuses_51257_while_one_exists()
    {
        await using var run = await Run.StartAsync();
        var (invoice, shipment) = await run.FinalizedInvoiceAsync("m");
        var guard = Assert.IsType<SqlOperation>(new AddCustomerReturns().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");

        await using (var scope = run.SalesStore(CompanyA))
        {
            await run.Service(scope.Store, CompanyA, Preparer, null).CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(
                invoice.Id, "IADE-SENTETIK-M", Day, [new CreateSalesCustomerReturnLineRequest(invoice.Lines[0].Id, "1")], shipment),
                Metadata("k-create"));
        }

        Assert.Equal(51257, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, id => id.EndsWith("_AddCustomerReturns", StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[index]);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[index - 1]));
        Assert.Equal(51257, rollback.Number);
        Assert.Equal(applied[..(index + 1)], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturns]"));
    }

    [SqlServerFact]
    public async Task A_pending_customer_return_is_rejected_by_another_actor_and_a_draft_is_cancelled_and_neither_takes_another_step_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (invoice, shipment) = await run.FinalizedInvoiceAsync("r");
        var line = invoice.Lines.Single(item => item.LineNo == 1);
        await using var scope = run.SalesStore(CompanyA);
        var preparer = run.Service(scope.Store, CompanyA, Preparer, null);
        var approver = run.Service(scope.Store, CompanyA, Approver, null);
        var rejectedId = (await preparer.CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-R1", Day,
            [new CreateSalesCustomerReturnLineRequest(line.Id, "3")], shipment), Metadata("k-create-r1"))).CustomerReturn.Id;
        await preparer.SubmitCustomerReturnAsync(rejectedId, new SalesDraftStateCommandRequest("1"), Metadata("k-submit-r1"));

        // SAT-D290 (3): the submitter's own rejection is refused and recorded under its key; nothing moves.
        var self = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            preparer.RejectCustomerReturnAsync(rejectedId, new SalesDraftStateCommandRequest("2"), Metadata("k-self-reject")));
        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval), (self.Status, self.Code));

        var rejected = await approver.RejectCustomerReturnAsync(rejectedId, new SalesDraftStateCommandRequest("2", "Belge eksik"),
            Metadata("k-reject"));
        Assert.Equal(("Rejected", "3", Approver, "Belge eksik"), (rejected.CustomerReturn.State, rejected.CustomerReturn.Version,
            rejected.CustomerReturn.RejectedBy, rejected.CustomerReturn.RejectionReason));
        var replay = await approver.RejectCustomerReturnAsync(rejectedId, new SalesDraftStateCommandRequest("2", "Belge eksik"),
            Metadata("k-reject"));
        Assert.True(replay.IsReplay);
        Assert.Equal((5, "Belge eksik", 1, 1), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", rejectedId)),
            await run.ScalarAsync<string>("SELECT [RejectionReason] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", rejectedId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [CommandName] = N'"
                + SalesCustomerReturnCommands.RejectV1 + "' AND [Outcome] = 2"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.CustomerReturn' "
                + "AND [EventType] = N'sales.customer-return.rejected.v1' AND [ActorId] = @actor", ("@actor", Approver))));

        // The rejected return is terminal and consumed nothing: a new return of the whole line is recorded and cancelled by its preparer.
        var cancelState = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            preparer.CancelCustomerReturnAsync(rejectedId, new SalesDraftStateCommandRequest("3"), Metadata("k-cancel-rejected")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (cancelState.Status, cancelState.Code));
        var draft = await preparer.CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-R2", Day,
            [new CreateSalesCustomerReturnLineRequest(line.Id, "3")], shipment), Metadata("k-create-r2"));
        var cancelled = await preparer.CancelCustomerReturnAsync(draft.CustomerReturn.Id, new SalesDraftStateCommandRequest("1"),
            Metadata("k-cancel"));
        Assert.Equal(("Cancelled", "2", Preparer, (string?)null), (cancelled.CustomerReturn.State, cancelled.CustomerReturn.Version,
            cancelled.CustomerReturn.CancelledBy, cancelled.CustomerReturn.CancellationReason));

        // TR_CustomerReturns_Guard: a rejected or cancelled return takes no further step, even through raw SQL (51255).
        Assert.Equal(51255, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturns] SET [RejectionReason] = N'SENTETIK-other', [Version] = [Version] + 1 WHERE [Id] = @id; SELECT 1",
            ("@id", rejectedId)))).Number);
        Assert.Equal(51255, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturns] SET [CancellationReason] = N'SENTETIK-other', [Version] = [Version] + 1 WHERE [Id] = @id; "
            + "SELECT 1", ("@id", draft.CustomerReturn.Id)))).Number);

        // A draft never jumps to Rejected (51255) and the preparer never rejects even through raw SQL (CK_CustomerReturns_MakerChecker).
        var third = (await preparer.CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(invoice.Id, "IADE-SENTETIK-R3", Day,
            [new CreateSalesCustomerReturnLineRequest(line.Id, "1")], shipment), Metadata("k-create-r3"))).CustomerReturn.Id;
        Assert.Equal(51255, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturns] SET [State] = 5, [SubmittedBy] = N'SENTETIK-s', [SubmittedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[RejectedBy] = N'SENTETIK-r', [RejectedAtUtc] = SYSDATETIMEOFFSET(), [Version] = 3 WHERE [Id] = @id; SELECT 1",
            ("@id", third)))).Number);
        await preparer.SubmitCustomerReturnAsync(third, new SalesDraftStateCommandRequest("1"), Metadata("k-submit-r3"));
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[CustomerReturns] SET [State] = 5, [RejectedBy] = [CreatedBy], [RejectedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[Version] = 3 WHERE [Id] = @id; SELECT 1", ("@id", third)))).Number);
        Assert.Equal((5, 6, 2), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", rejectedId)),
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", draft.CustomerReturn.Id)),
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", third))));
    }

    [SqlServerFact]
    public async Task The_rejection_migrations_Down_passes_with_no_rejected_or_cancelled_return_and_refuses_51258_once_one_exists()
    {
        await using var run = await Run.StartAsync();
        var (invoice, shipment) = await run.FinalizedInvoiceAsync("n");
        var guard = Assert.IsType<SqlOperation>(new AddCustomerReturnRejectionAndCancellation().DownOperations[0]).Sql;
        Guid id;
        await using (var scope = run.SalesStore(CompanyA))
        {
            id = (await run.Service(scope.Store, CompanyA, Preparer, null).CreateCustomerReturnAsync(new CreateSalesCustomerReturnRequest(
                invoice.Id, "IADE-SENTETIK-N", Day, [new CreateSalesCustomerReturnLineRequest(invoice.Lines[0].Id, "1")], shipment),
                Metadata("k-create"))).CustomerReturn.Id;
        }

        // An open draft does not block the rollback.
        await run.ScalarAsync<int>(guard + " SELECT 1");

        await using (var scope = run.SalesStore(CompanyA))
        {
            await run.Service(scope.Store, CompanyA, Preparer, null)
                .CancelCustomerReturnAsync(id, new SalesDraftStateCommandRequest("1"), Metadata("k-cancel"));
        }

        Assert.Equal(51258, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, migration => migration.EndsWith("_AddCustomerReturnRejectionAndCancellation", StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[index]);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[index - 1]));
        Assert.Equal(51258, rollback.Number);
        Assert.Equal(applied[..(index + 1)], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(6, await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id] = @id", ("@id", id)));
    }

    /// <summary>A raw copy of the stored draft return under another id with the given customer document (no attempt references it).</summary>
    private static string CopyOf(string documentNo) =>
        "INSERT INTO [sales].[CustomerReturns] ([Id],[CompanyId],[OriginalInvoiceId],[OriginalDocumentNo],[PartyId],[CompanyAccountId],"
        + "[CurrencyId],[AmountScale],[ExchangeRate],[FunctionalCurrencyId],[FunctionalScale],[ShipmentId],[CustomerDocumentNo],"
        + "[CustomerDocumentDate],[State],[CreatedBy],[CreatedAtUtc],[NetAmount],[VatAmount],[WithholdingAmount],[LineTotalAmount],"
        + "[FunctionalNetAmount],[FunctionalVatAmount],[FunctionalWithholdingAmount],[FunctionalLineTotalAmount],[NeedsReview],[LineCount],"
        + "[LinesJson],[Version],[UpdatedAtUtc],[CorrelationId]) SELECT NEWID(),[CompanyId],[OriginalInvoiceId],[OriginalDocumentNo],"
        + "[PartyId],[CompanyAccountId],[CurrencyId],[AmountScale],[ExchangeRate],[FunctionalCurrencyId],[FunctionalScale],[ShipmentId],"
        + documentNo + ",[CustomerDocumentDate],[State],[CreatedBy],[CreatedAtUtc],[NetAmount],[VatAmount],[WithholdingAmount],"
        + "[LineTotalAmount],[FunctionalNetAmount],[FunctionalVatAmount],[FunctionalWithholdingAmount],[FunctionalLineTotalAmount],"
        + "[NeedsReview],[LineCount],[LinesJson],[Version],[UpdatedAtUtc],[CorrelationId] FROM [sales].[CustomerReturns] WHERE [Id] = @id; "
        + "SELECT 1";

    private static SalesCommandMetadata Metadata(string key) =>
        new(key, "sales-customer-return-" + key, null, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));

    private static string Hash(char fill) => new(fill, 64);

    /// <summary>A USD invoice of two 20 % lines (3 kg at 33.33 and 1 kg at 10.01, two shipments) at 34.5678 — SYNTHETIC shares.</summary>
    private static SalesInvoice Invoice(Guid id, string number, string actor, Guid shipment1, Guid shipment2)
    {
        var tax = new SalesLineTaxSnapshot(Product, Day, "Normal", "Steel", "Resolved", null, "Record", Guid.NewGuid(), 1, "Resolved", 20m,
            "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
        SalesInvoiceShareInput Share(Guid shipment, int lineNo, string quantity, string price) => new(Guid.NewGuid(), shipment,
            Guid.NewGuid(), "IRS-2026-000001", lineNo, Day, Guid.NewGuid(), Guid.NewGuid(), 0, Product, Kg, decimal.Parse(quantity,
                System.Globalization.CultureInfo.InvariantCulture), quantity, new CommercialDraftFields(price, Kg, null), tax);
        var terms = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 30, "100")]);
        return SalesInvoice.Create(id, CompanyA, number,
            new SalesInvoiceHeader(Guid.NewGuid(), Guid.NewGuid(), Usd, Guid.NewGuid(), "1", 2, Day,
                new SalesInvoiceRateSnapshot(34.5678m, Day, Day.AddDays(-1), Try, 2)),
            [Share(shipment1, 1, "3", "33.33"), Share(shipment2, 2, "1", "10.01")],
            SalesDueDateCalculator.Compute(terms, Day, Day).DueDates, terms, actor, DateTimeOffset.UtcNow);
    }

    /// <summary>The SYNTHETIC grants: the four return names and the price read (every API-40 read and command needs it).</summary>
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission is SalesPermissionManifestV1.ReturnsRead or SalesPermissionManifestV1.ReturnsWrite
                or SalesPermissionManifestV1.ReturnsApprove or SalesPermissionManifestV1.ReturnsFinalize or SalesPermissionManifestV1.OrdersPriceRead
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-SALES"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    /// <summary>A SYNTHETIC Finance reversal port that admits every offer (the real port is not bound yet, SAT-D294).</summary>
    private sealed class FinancePort : ISalesCustomerReturnFinancePortV1
    {
        public int Calls { get; private set; }

        public Task<SalesCustomerReturnFinanceAnswerV1> OfferAsync(SalesCustomerReturnFinanceOfferV1 offer, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SalesCustomerReturnFinanceAnswerV1(offer.CustomerReturnId, SalesCustomerReturnFinanceOutcomeV1.Admitted, null));
        }
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public SalesStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-invoicer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-api-40-{Guid.NewGuid():N}";

        /// <summary>The real host composed over a fresh database (health is probed so the migrations have run).</summary>
        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>
        /// An approved, matched and finalised invoice of company A through the real Sales store, and the shipment of its first line. The
        /// official number and ETTN are unique per company, so each run builds one invoice.
        /// </summary>
        public async Task<(SalesInvoice Invoice, Guid Shipment)> FinalizedInvoiceAsync(string prefix)
        {
            var invoiceId = Guid.NewGuid();
            var shipment = Guid.NewGuid();
            var approver = "SENTETIK-invoice-approver-" + Guid.NewGuid().ToString("N");
            var matcher = "SENTETIK-matcher-" + Guid.NewGuid().ToString("N");
            var finalizer = "SENTETIK-invoice-finalizer-" + Guid.NewGuid().ToString("N");
            await using var scope = SalesStore(CompanyA);
            ISalesInvoiceStore invoices = scope.Store;
            await invoices.StoreInvoiceAsync(Envelope(prefix + "-create"), Hash('a'),
                number => Invoice(invoiceId, number, Actor, shipment, Guid.NewGuid()));
            await invoices.DecideInvoiceAsync(Envelope(prefix + "-submit"), Hash('b'), SalesInvoiceCommands.SubmitV1, invoiceId,
                (invoice, now) => invoice.Submit(1, Actor, now), new SalesDraftStateAudit("sales.invoice.submitted.v1", "Submit", "SENTETIK"));
            var approved = (await invoices.DecideInvoiceAsync(Envelope(prefix + "-approve", approver, SalesPermissionManifestV1.InvoicesApprove),
                Hash('c'), SalesInvoiceCommands.ApproveV1, invoiceId, (invoice, now) => invoice.Approve(2, approver, now),
                new SalesDraftStateAudit("sales.invoice.approved.v1", "Approve", "SENTETIK")))!.Invoice;
            var official = new SalesOfficialInvoiceFacts("GIB2026000000401", "4c5d6e7f-8a9b-4c0d-9e1f-2a3b4c5d6e7f", approved.Header.PartyId,
                approved.Header.CurrencyId, approved.PayableFunctionalAmount, approved.PayableFunctionalAmount, approved.VatFunctionalAmount,
                approved.WithholdingFunctionalAmount);
            await invoices.MatchInvoiceAsync(Envelope(prefix + "-match", matcher, SalesPermissionManifestV1.InvoicesMatch), Hash('1'),
                invoiceId, (invoice, now) => invoice.RecordMatch(3, matcher, official, 0.05m, false, null, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "SENTETIK"));
            var finalized = await invoices.FinalizeInvoiceAsync(Envelope(prefix + "-final", finalizer, SalesPermissionManifestV1.InvoicesFinalize),
                Hash('2'), invoiceId, (invoice, now) => invoice.Finalize(3, finalizer, Guid.NewGuid, now),
                new SalesDraftStateAudit("sales.invoice.finalized.v1", "Finalize", "SENTETIK"));
            Assert.Equal(SalesInvoiceState.Finalized, finalized!.Invoice.State);
            return (finalized.Invoice, shipment);
        }

        public SalesDraftWriteEnvelope Envelope(string key, string? actor = null, string permission = SalesPermissionManifestV1.InvoicesWrite) =>
            new(actor ?? Actor, permission, ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public CompanyContext Company(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return companyContext;
        }

        /// <summary>The REAL customer return service over a real store as the given SYNTHETIC actor, with or without a Finance port.</summary>
        public SalesCustomerReturnService Service(SalesStore store, Guid company, string actor, ISalesCustomerReturnFinancePortV1? finance) =>
            new(Company(company), new FixedUser(actor, company), new Authority(), new FixedCorrelation(Correlation), store, store,
                TimeProvider.System, finance);

        public SalesDbContext SalesContext(Guid company) =>
            new(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, Company(company));

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = Company(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
