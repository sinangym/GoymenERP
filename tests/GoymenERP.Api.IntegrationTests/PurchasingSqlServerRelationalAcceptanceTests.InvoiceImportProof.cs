using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Imported_invoice_retains_interpretation_proof_in_SQL_history_and_replay_without_Finance_or_outbox_effects()
    {
        await using var run = await Run.StartAsync();
        var original = IncomingInvoice(run);
        var proof = new PurchaseInvoiceImportProof(original.Id, "purchase-invoice-file-reader.v1", "{\"format\":2}",
            "[{\"sourceLineId\":\"source-01\"}]", Guid.NewGuid(), 2, Guid.NewGuid(), 3, "[]", null);
        var invoice = (PurchaseIncomingInvoiceSnapshot.Capture(original) with { Source = original.Source with { ImportProof = proof } }).Restore();
        var envelope = run.Envelope("import-proof"); var hash = Hash("import-proof");
        await using (var scope = run.Store(CompanyA))
        {
            var result = await ((IPurchasingIncomingInvoiceStoreV1)scope.Store).RecordIncomingInvoiceAsync(invoice, envelope, hash, _ => Task.CompletedTask);
            Assert.False(result.IsReplay); Assert.Equal(proof, result.Invoice!.Source.ImportProof);
        }
        await using (var fresh = run.Store(CompanyA))
        {
            var owner = (IPurchasingIncomingInvoiceStoreV1)fresh.Store;
            var persisted = await owner.FindIncomingInvoiceAsync(invoice.Id);
            Assert.Equal(proof, persisted!.Source.ImportProof);
            Assert.Equal(proof, JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(JsonSerializer.Serialize(persisted))!.Restore().Source.ImportProof);
            var replay = await owner.FindIncomingInvoiceAttemptAsync(PurchaseIncomingInvoiceCommandsV1.Record, envelope, hash);
            Assert.True(replay!.IsReplay); Assert.Equal(proof, replay.Invoice!.Source.ImportProof);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.IncomingInvoice' AND EntityId=@id", ("@id", invoice.Id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE AggregateId=@id", ("@id", invoice.Id)));
    }
}
