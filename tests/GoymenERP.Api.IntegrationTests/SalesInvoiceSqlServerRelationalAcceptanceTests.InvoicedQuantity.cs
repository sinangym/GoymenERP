using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Order_invoiced_quantity_uses_actual_finalized_share_proof_and_excludes_the_same_claim_after_official_cancellation()
    {
        await using var run = await Run.StartAsync();
        var actor = Guid.NewGuid().ToString("D");
        var invoice = await FinalizedAsync(run, "quantity-proof", "SENTETIK-QTY-1", Guid.NewGuid().ToString("D"),
            "SENTETIK-quantity-checker", actor);
        var line = invoice.Lines[0];
        var orderId = line.Share.SalesOrderId;
        var read = await ReadOrderInvoiceQuantityAsync(run, CompanyA, orderId);
        Assert.Equal(SalesOrderInvoicedQuantityReadOutcomeV1.Available, read.Outcome);
        var actual = Assert.Single(read.Lines);
        Assert.Equal((invoice.Id, line.Share.LineId, line.Share.SalesOrderLineId, line.Share.UnitId, 3m, "Normal"),
            (actual.InvoiceId, actual.InvoiceLineId, actual.SalesOrderLineId, actual.UnitId, actual.Quantity, actual.InvoiceKind));
        Assert.StartsWith(ISalesOrderInvoicedQuantityReadPortV1.SourceVersionPrefix, read.SourceVersion!);
        var repeated = await ReadOrderInvoiceQuantityAsync(run, CompanyA, orderId);
        Assert.Equal(read.SourceVersion, repeated.SourceVersion);
        Assert.Equal(read.Lines.ToArray(), repeated.Lines.ToArray());

        var foreign = await ReadOrderInvoiceQuantityAsync(run, CompanyB, orderId);
        Assert.Equal(SalesOrderInvoicedQuantityReadOutcomeV1.Available, foreign.Outcome);
        Assert.Empty(foreign.Lines);

        await using (var owner = run.OfficialStore(CompanyA))
            await owner.Store.RecordAsync(run.Envelope("quantity-official-cancel", actor: actor, permission: SalesPermissionManifestV1.InvoicesFinalize),
                Hash('f'), SalesOfficialInvoiceKindV1.Normal, invoice.Id, invoice.Version, "SENTETIK gerçek yaşam döngüsü", OfficialProof(invoice),
                _ => Task.CompletedTask);
        var released = await ReadOrderInvoiceQuantityAsync(run, CompanyA, orderId);
        Assert.Equal(SalesOrderInvoicedQuantityReadOutcomeV1.Available, released.Outcome);
        Assert.Empty(released.Lines);
        Assert.NotEqual(read.SourceVersion, released.SourceVersion);
        // The cancellation releases the original share, not its immutable finalization or physical quantity.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE InvoiceId=@id", ("@id", invoice.Id)));
        Assert.Equal(3m, await run.ScalarAsync<decimal>("SELECT Quantity FROM sales.InvoiceLines WHERE InvoiceId=@id AND LineNo=1", ("@id", invoice.Id)));
    }

    [SqlServerFact]
    public async Task Order_invoice_quantity_does_not_release_evidence_after_its_consumer_company_fence_changes()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var reader = scope.ServiceProvider.GetRequiredService<ISalesOrderInvoicedQuantityReadPortV1>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(CompanyA, Guid.NewGuid(), _ =>
        {
            company.Set(CompanyB);
            return Task.CompletedTask;
        }));
    }

    [SqlServerFact]
    public async Task Order_invoice_quantity_is_unavailable_when_an_actual_official_cancellation_changes_its_discovery_after_the_claim_read()
    {
        await using var run = await Run.StartAsync();
        var actor = Guid.NewGuid().ToString("D");
        var invoice = await FinalizedAsync(run, "quantity-race", "SENTETIK-QTY-RACE", Guid.NewGuid().ToString("D"),
            "SENTETIK-quantity-checker", actor);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var claims = scope.ServiceProvider.GetRequiredService<ISalesFinalizedShipmentClaimReadPortV1>();
        var reader = new SalesOrderInvoicedQuantityReadPortV1(scope.ServiceProvider.GetRequiredService<SalesDbContext>(),
            company, scope.ServiceProvider.GetRequiredService<ISalesSubcontractServiceInvoiceStore>(),
            new AfterInvoiceQuantityClaimRead(claims, async () =>
            {
                await using var owner = run.OfficialStore(CompanyA);
                await owner.Store.RecordAsync(run.Envelope("quantity-race-cancel", actor: actor,
                        permission: SalesPermissionManifestV1.InvoicesFinalize), Hash('e'), SalesOfficialInvoiceKindV1.Normal,
                    invoice.Id, invoice.Version, "SENTETIK kaynak okuma arası iptal", OfficialProof(invoice), _ => Task.CompletedTask);
            }));

        // The hook orders two genuine owner operations; it is not a SQL concurrency/stress execution claim.
        var answer = await reader.ReadAsync(CompanyA, invoice.Lines[0].Share.SalesOrderId, _ => Task.CompletedTask);

        Assert.Equal(SalesOrderInvoicedQuantityReadOutcomeV1.Unavailable, answer.Outcome);
        Assert.Empty(answer.Lines);
        Assert.Null(answer.SourceVersion);
        Assert.Empty((await ReadOrderInvoiceQuantityAsync(run, CompanyA, invoice.Lines[0].Share.SalesOrderId)).Lines);
    }

    private sealed class AfterInvoiceQuantityClaimRead(ISalesFinalizedShipmentClaimReadPortV1 inner, Func<Task> after)
        : ISalesFinalizedShipmentClaimReadPortV1
    {
        public async Task<SalesShipmentClaimBatchV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> shipmentIds,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        {
            var answer = await inner.ReadAsync(companyId, shipmentIds, freshConsumerFence, cancellationToken);
            await after();
            return answer;
        }
    }

    private static async Task<SalesOrderInvoicedQuantityReadV1> ReadOrderInvoiceQuantityAsync(Run run, Guid companyId, Guid orderId)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(companyId);
        var reader = scope.ServiceProvider.GetRequiredService<ISalesOrderInvoicedQuantityReadPortV1>();
        return await reader.ReadAsync(companyId, orderId, _ => company.IsResolved && company.CompanyId == companyId
            ? Task.CompletedTask : throw new UnauthorizedAccessException());
    }
}
