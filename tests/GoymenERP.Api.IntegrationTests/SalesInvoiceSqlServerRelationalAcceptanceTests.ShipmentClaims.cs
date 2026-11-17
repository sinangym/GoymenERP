using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Shipment_claim_reader_binds_real_finalization_share_and_outbox_and_hides_foreign_company_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var invoice = await FinalizedAsync(run, "claim", "SENTETIK-CLAIM-1", Guid.NewGuid().ToString("D"),
            "SENTETIK-checker", "SENTETIK-finalizer");
        var ids = invoice.Lines.Select(x => x.Share.ShipmentId).ToArray(); var unknown = Guid.NewGuid();
        var payload = await run.ScalarAsync<string>("SELECT PayloadJson FROM sales.OutboxMessages WHERE CompanyId=@company AND AggregateId=@id AND EventType=N'sales.invoice.finalized.v1'",
            ("@company", CompanyA), ("@id", invoice.Id));
        var eventId = await run.ScalarAsync<Guid>("SELECT Id FROM sales.OutboxMessages WHERE CompanyId=@company AND AggregateId=@id AND EventType=N'sales.invoice.finalized.v1'",
            ("@company", CompanyA), ("@id", invoice.Id));
        await using (var scope = run.Store(CompanyA))
        {
            var company = new ClaimCompany(CompanyA); var reads = 0;
            var reader = new SalesFinalizedShipmentClaimReadPortV1(scope.Context, company);
            var result = await reader.ReadAsync(CompanyA, [.. ids, unknown], _ => { reads++; return Task.CompletedTask; });
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.Available, result.Outcome);
            Assert.Equal(3, result.Items.Count); Assert.Equal(18, reads);
            foreach (var line in invoice.Lines)
            {
                var item = Assert.Single(result.Items, x => x.ShipmentId == line.Share.ShipmentId);
                var claim = Assert.IsType<SalesFinalizedShipmentClaimV1>(item.Claim);
                Assert.Equal((CompanyA, invoice.Id, line.Id, invoice.Finalization!.AdmissionId, invoice.Version),
                    (claim.CompanyId, claim.InvoiceId, claim.InvoiceLineId, claim.AdmissionId, claim.FinalizedVersion));
                Assert.Equal(eventId, claim.FinalizedEventId);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), claim.FinalizedPayloadSha256);
                Assert.Equal(line.Share.QuantityText, claim.ShipmentSource.InvoicedQuantity);
                Assert.Equal(0, claim.ShipmentSource.PriceBasisRevision);
                Assert.Equal("Finalized", claim.Lifecycle.State); Assert.Null(claim.Lifecycle.CancelledAtUtc);
            }
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, Assert.Single(result.Items, x => x.ShipmentId == unknown).Outcome);
        }
        await using (var scope = run.Store(CompanyB))
        {
            var reader = new SalesFinalizedShipmentClaimReadPortV1(scope.Context, new ClaimCompany(CompanyB));
            var result = await reader.ReadAsync(CompanyB, ids, _ => Task.CompletedTask);
            Assert.All(result.Items, x => { Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, x.Outcome); Assert.Null(x.Claim); Assert.Empty(x.Lifecycles); });
            var bounded = await reader.ReadAsync(CompanyB, Enumerable.Range(0, 128).Select(_ => Guid.NewGuid()).ToArray(), _ => Task.CompletedTask);
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.Available, bounded.Outcome);
            Assert.Equal(128, bounded.Items.Count);
            Assert.All(bounded.Items, x => Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, x.Outcome));
        }
        Assert.Equal(payload, await run.ScalarAsync<string>("SELECT PayloadJson FROM sales.OutboxMessages WHERE Id=@id", ("@id", eventId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OutboxMessages WHERE CompanyId=@company AND AggregateId=@id AND EventType=N'sales.invoice.finalized.v1'",
            ("@company", CompanyA), ("@id", invoice.Id)));
    }

    [SqlServerFact]
    public async Task Shipment_claim_reader_rechecks_real_consumer_fence_after_database_await_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var invoice = await FinalizedAsync(run, "claim-fence", "SENTETIK-CLAIM-2", Guid.NewGuid().ToString("D"),
            "SENTETIK-checker", "SENTETIK-finalizer");
        foreach (var change in new[] { "company", "actor", "membership", "permission" })
        {
            await using var scope = run.Store(CompanyA);
            var company = new ClaimCompany(CompanyA); var calls = 0; var actor = "SENTETIK-owner";
            var member = true; var permission = true;
            Task Fence(CancellationToken token)
            {
                if (++calls == 3)
                {
                    if (change == "company") company.Set(CompanyB);
                    if (change == "actor") actor = "SENTETIK-other";
                    if (change == "membership") member = false;
                    if (change == "permission") permission = false;
                }
                if (actor != "SENTETIK-owner" || !member || !permission) throw new UnauthorizedAccessException();
                return Task.CompletedTask;
            }
            var result = await new SalesFinalizedShipmentClaimReadPortV1(scope.Context, company)
                .ReadAsync(CompanyA, [invoice.Lines[0].Share.ShipmentId], Fence);
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.Unavailable, result.Outcome);
            Assert.Empty(result.Items); Assert.Null(result.SourceVersion); Assert.Equal(3, calls);
        }
    }

    [SqlServerFact]
    public async Task Shipment_claim_reader_detects_lifecycle_change_and_reports_actual_cancelled_history_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var id = Guid.NewGuid(); var shipment = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
            await scope.Invoices.StoreInvoiceAsync(run.Envelope("claim-draft"), Hash('a'),
                number => Invoice(id, number, run.Actor, shipment, Guid.NewGuid()));
        await using (var scope = run.Store(CompanyA))
        {
            var calls = 0;
            async Task Fence(CancellationToken token)
            {
                // A real concurrent owner mutation between the two bounded snapshots.
                if (++calls != 10) return;
                await using var writer = run.Store(CompanyA);
                await writer.Invoices.DecideInvoiceAsync(run.Envelope("claim-cancel"), Hash('b'), SalesInvoiceCommands.CancelV1, id,
                    (invoice, now) => invoice.Cancel(1, run.Actor, "SENTETIK vazgecildi", now),
                    new SalesDraftStateAudit("sales.invoice.cancelled.v1", "Cancel", "SENTETIK"));
            }
            var result = await new SalesFinalizedShipmentClaimReadPortV1(scope.Context, new ClaimCompany(CompanyA))
                .ReadAsync(CompanyA, [shipment], Fence);
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.Unavailable, result.Outcome);
            Assert.Equal("shipment_claim_changed", result.Reason); Assert.Empty(result.Items);
        }
        await using (var scope = run.Store(CompanyA))
        {
            var result = await new SalesFinalizedShipmentClaimReadPortV1(scope.Context, new ClaimCompany(CompanyA))
                .ReadAsync(CompanyA, [shipment], _ => Task.CompletedTask);
            var item = Assert.Single(result.Items);
            Assert.Equal(SalesShipmentClaimReadOutcomeV1.NotInvoiced, item.Outcome); Assert.Null(item.Claim);
            var lifecycle = Assert.Single(item.Lifecycles);
            Assert.Equal((id, 2L, "Cancelled", run.Actor), (lifecycle.InvoiceId, lifecycle.Version, lifecycle.State, lifecycle.CancelledBy));
            Assert.Equal(lifecycle.CancelledAtUtc, lifecycle.ReleasedAtUtc); Assert.NotNull(lifecycle.CancelledAtUtc);
        }
    }

    private sealed class ClaimCompany(Guid id) : ICompanyContext
    {
        public Guid CompanyId { get; private set; } = id;
        public bool IsResolved => CompanyId != Guid.Empty;
        public void Set(Guid value) => CompanyId = value;
    }
}
