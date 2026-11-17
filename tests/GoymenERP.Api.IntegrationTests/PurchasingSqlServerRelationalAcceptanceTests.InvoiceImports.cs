using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Invoice_import_reservation_replays_in_company_and_down_refuses_while_its_document_owner_identity_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseInvoiceImportReservations().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var envelope = run.Envelope("invoice-reservation") with { PermissionCode = PurchaseIncomingInvoicePermissionNamesV1.Import };
        Guid id;
        await using (var scope = run.Store(CompanyA))
        {
            var store = (IPurchaseInvoiceImportReservationStoreV1)scope.Store;
            var first = await store.ReserveAsync(envelope, _ => Task.CompletedTask);
            var replay = await store.ReserveAsync(envelope with { CorrelationId = "synthetic-retry" }, _ => Task.CompletedTask);
            id = first.Reservation.Id;
            Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
            Assert.Equal(first.Reservation, replay.Reservation); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
            var conflict = await Assert.ThrowsAsync<PurchasingStoreException>(() => store.ReserveAsync(
                envelope with { ActorId = "synthetic-other" }, _ => Task.CompletedTask));
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, conflict.Code);
        }
        await using (var scope = run.Store(CompanyB)) Assert.Null(await ((IPurchaseInvoiceImportReservationStoreV1)scope.Store).FindAsync(id));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceImportReservations"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.InvoiceImportReservation' AND EntityId=@id", ("@id", id.ToString("D"))));
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51996, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceImportReservations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices"));
    }

    [SqlServerFact]
    public async Task Invoice_import_reservation_refuses_borrowed_transaction_or_revoked_authority_without_row_or_audit()
    {
        await using var run = await Run.StartAsync();
        var envelope = run.Envelope("invoice-reservation-refused") with { PermissionCode = PurchaseIncomingInvoicePermissionNamesV1.Import };
        await using (var scope = run.Store(CompanyA))
        {
            await using var transaction = await scope.Context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => ((IPurchaseInvoiceImportReservationStoreV1)scope.Store)
                .ReserveAsync(envelope, _ => Task.CompletedTask));
            await transaction.CommitAsync();
        }
        await using (var scope = run.Store(CompanyA))
        {
            var calls = 0;
            await Assert.ThrowsAsync<PurchasingServiceException>(() => ((IPurchaseInvoiceImportReservationStoreV1)scope.Store)
                .ReserveAsync(envelope, _ => { if (++calls == 3) throw new PurchasingServiceException(403, "forbidden"); return Task.CompletedTask; }));
            Assert.Empty(scope.Context.ChangeTracker.Entries());
        }
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceImportReservations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.InvoiceImportReservation'"));
    }
}
