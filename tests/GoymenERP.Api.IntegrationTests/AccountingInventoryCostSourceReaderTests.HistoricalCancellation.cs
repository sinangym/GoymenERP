using GoymenERP.Api.AccountingRuntime;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Inbox;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInventoryCostSourceReaderTests
{
    [Fact]
    public async Task Deferred_actual_cost_can_carry_exact_historical_cancellation_without_restoring_an_active_sales_claim()
    {
        var f = new Fixture(); var original = f.Inventory.Value.Lines[0];
        f.Inventory.Value = f.Inventory.Value with { Lines = [original with { Tl = original.Tl with {
            Status = InventoryMovementCostStampStatusV1.Unavailable, Amount = null, UnitCostRatio = null, Reason = "unknown-at-shipment" } }] };
        var envelope = f.Envelope(); var posted = new PostedPort(f);
        var claims = new FinalizedClaims(f, envelope) { Historical = true };
        var reader = new AccountingInventoryCostSourceReaderV1(f.Company, f.Logistics, f.Inventory, posted, claims);
        var source = await reader.ReadAsync(envelope);
        Assert.Equal(claims.CancellationId, source.HistoricalCancellation!.CancellationId);
        Assert.Equal(claims.CancelledBy, source.HistoricalCancellation.CancelledBy);
        Assert.Equal(envelope.OccurredAtUtc.AddMinutes(1), source.HistoricalCancellation.CancelledAtUtc);
        Assert.Equal(posted.Share, Assert.Single(Assert.Single(source.Components).AlreadyPostedComponents!));
        Assert.Null(Assert.Single(source.Components).UnpostedTlAmount);
        claims.DuplicateHistory = true;
        Assert.Equal("sales_cost_owner_claim_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => reader.ReadAsync(envelope))).Reason);
        claims.DuplicateHistory = false; claims.WrongEvent = true;
        Assert.Equal("sales_cost_owner_claim_unavailable", (await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(
            () => reader.ReadAsync(envelope))).Reason);
    }
}
