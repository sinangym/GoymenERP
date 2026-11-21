using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Delayed_cancelled_known_stamp_source_does_not_reverse_or_duplicate_the_new_invoice_actual_initial_cogs_journal()
    {
        await using var run = await Run.Start(); var period = await run.Period();
        await using var scope = run.Open();
        var physical = new AccountingSalesPhysicalCostClaimV1(run.Source.Facts.ShipmentId, Guid.NewGuid(), 1, run.Source.Facts.ProductId, 3m);
        var replacement = run.Source.Facts with { InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(), FinalizationAdmissionId = Guid.NewGuid() };
        var current = new CostReader(replacement); var currentStore = Costs(scope, new HistoricalKnownPhysicalCostReader(current, physical));
        await currentStore.AcceptAsync(current.Envelope);
        var item = Assert.Single(await currentStore.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        await currentStore.PrepareAsync(item.ProposalId, item.Kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        var posted = await currentStore.ApproveAsync(item.ProposalId, item.Kind, 2, Checker, Fence, Metadata(Checker));
        Assert.NotNull(posted.JournalId);
        var cancellation = HistoricalCancellationRequest(run.Source.Facts); await RecordHistoricalCancellation(scope, cancellation);
        var previous = new CostReader(run.Source.Facts);
        var previousStore = Costs(scope, new HistoricalCostReader(new HistoricalKnownPhysicalCostReader(previous, physical), cancellation));
        await previousStore.AcceptAsync(previous.Envelope); await previousStore.AcceptAsync(previous.Envelope);
        var unchanged = Assert.Single(await currentStore.ListAsync(item.Kind));
        Assert.Equal(posted.JournalId, unchanged.JournalId); Assert.Equal("Posted", unchanged.Status);
        Assert.Equal(2, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(0, await run.Count("accounting.InventoryCostCancellationVersions"));
        var receipt = await ReadCostReceipt(run, previous.Envelope.EventId);
        Assert.Equal(posted.ProposalId, Assert.Single(Assert.Single(receipt.ObsoleteComponents!).Owners).ProposalId);
    }

    [SqlServerFact]
    public async Task A_cancelled_never_accepted_invoice_acknowledges_its_event_without_taking_or_reversing_the_actual_competing_invoice_cost_claim()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var replacement = run.Source.Facts with { InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(), FinalizationAdmissionId = Guid.NewGuid() };
        var replacementReader = new AttributedCostReader(replacement, ledger, physical);
        var replacementStore = AttributedCosts(scope, new CancellationCostReader(replacementReader, replacement), ledger);
        await replacementStore.AcceptAsync(replacementReader.Envelope);
        var winner = Assert.Single(await replacementStore.ListAsync(AccountingInventoryCostKindV1.SalesInvoicedShipment));
        var cancellation = HistoricalCancellationRequest(run.Source.Facts); await RecordHistoricalCancellation(scope, cancellation);
        var original = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var originalStore = AttributedCosts(scope,
            new HistoricalCostReader(new CancellationCostReader(original, run.Source.Facts), cancellation), ledger);
        await originalStore.AcceptAsync(original.Envelope); await originalStore.AcceptAsync(original.Envelope);
        // The SQL reread rebuilds nested collection instances; compare the complete value graph.
        Assert.Equal(JsonSerializer.Serialize(winner),
            JsonSerializer.Serialize(Assert.Single(await replacementStore.ListAsync(winner.Kind))));
        Assert.Equal(2, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(1, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal(1, await run.Count("accounting.Journals")); // Only the original posted manufacturing aggregate.
        Assert.Equal(0, await run.Count("accounting.InventoryCostCancellationVersions"));
        Assert.Equal(0, await run.Count("accounting.InventoryCostCancellationClaims"));
        var receipt = await ReadCostReceipt(run, original.Envelope.EventId);
        Assert.Equal(cancellation.CancellationId, receipt.HistoricalCancellation!.CancellationId);
        var component = Assert.Single(receipt.ObsoleteComponents!);
        Assert.Equal(AccountingInventoryCostTraceV1.SalesComponent(run.Source.Facts.InvoiceLineId), component.ComponentKey);
        var owner = Assert.Single(component.Owners);
        Assert.Equal((replacementReader.Envelope.EventId, replacement.InvoiceId, replacement.InvoiceLineId,
            replacement.FinalizationAdmissionId, winner.ProposalId),
            (owner.EventId, owner.InvoiceId, owner.InvoiceLineId, owner.AdmissionId, owner.ProposalId));
        Assert.Equal((physical.Target.ShipmentId, physical.Target.MovementId, 1, 1m),
            (owner.Physical.ShipmentId, owner.Physical.MovementId, owner.Physical.MovementLineNumber, owner.Physical.QuantityKg));
        Assert.Equal(64, owner.SourceSha256.Length); Assert.Equal(64, owner.ClaimSha256.Length);
    }

    [SqlServerFact]
    public async Task A_cancelled_historical_event_cannot_discard_cost_when_the_competing_owner_physical_proof_differs()
    {
        await using var run = await Run.Start(); var period = await run.Period(); var physical = new AttributionLineage(run.Source.Facts);
        await using var scope = run.Open(); var ledger = AttributionLedger(scope, physical);
        await PostAttributed(scope, physical, ledger, period);
        var replacement = run.Source.Facts with { InvoiceId = Guid.NewGuid(), InvoiceLineId = Guid.NewGuid(), FinalizationAdmissionId = Guid.NewGuid() };
        var replacementReader = new AttributedCostReader(replacement, ledger, physical);
        await AttributedCosts(scope, new DifferentPhysicalOwner(replacementReader), ledger).AcceptAsync(replacementReader.Envelope);
        var cancellation = HistoricalCancellationRequest(run.Source.Facts); await RecordHistoricalCancellation(scope, cancellation);
        var original = new AttributedCostReader(run.Source.Facts, ledger, physical);
        var store = AttributedCosts(scope, new HistoricalCostReader(new CancellationCostReader(original, run.Source.Facts), cancellation), ledger);
        var error = await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() => store.AcceptAsync(original.Envelope));
        Assert.Equal("inventory_cost_obsolete_owner_unavailable", error.Reason);
        Assert.Equal(1, await run.Count("accounting.InventoryCostSources"));
        Assert.Equal(1, await run.Count("accounting.InventoryCostProposals"));
        Assert.Equal(1, await run.Count("accounting.InvoiceCogsEconomicClaims"));
        Assert.Equal(1, await run.Count("accounting.Journals"));
        Assert.Equal(0, await run.Count("accounting.InventoryCostCancellationVersions"));
    }

    private static async Task<AccountingInventoryCostSourceV1> ReadCostReceipt(Run run, Guid eventId)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT SourceJson FROM accounting.InventoryCostSources WHERE CompanyId=@company AND EventId=@event", connection);
        command.Parameters.AddWithValue("@company", Company); command.Parameters.AddWithValue("@event", eventId);
        return JsonSerializer.Deserialize<AccountingInventoryCostSourceV1>((string)(await command.ExecuteScalarAsync())!, Json)!;
    }

    // Deliberately inconsistent synthetic external owner evidence. Both accepted Accounting source and claim are real;
    // the late event must not treat the same movement identity with a different physical quantity as its own proof.
    private sealed class DifferentPhysicalOwner(IAccountingInventoryCostSourceReaderV1 inner) : IAccountingInventoryCostSourceReaderV1
    {
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            var source = await inner.ReadAsync(envelope, cancellationToken);
            return source with { Components = source.Components.Select(x => x with { AlreadyPostedComponents = null,
                SalesPhysicalClaims = x.SalesPhysicalClaims!.Select(y => y with { QuantityKg = 2m }).ToArray() }).ToArray() };
        }
    }

    private sealed class HistoricalKnownPhysicalCostReader(IAccountingInventoryCostSourceReaderV1 inner, AccountingSalesPhysicalCostClaimV1 physical)
        : IAccountingInventoryCostSourceReaderV1
    {
        public async Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
        {
            var source = await inner.ReadAsync(envelope, cancellationToken);
            return source with { Components = source.Components.Select(x => x with { SalesPhysicalClaims = [physical] }).ToArray() };
        }
    }
}
