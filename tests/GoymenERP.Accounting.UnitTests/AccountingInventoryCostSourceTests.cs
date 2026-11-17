using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingInventoryCostSourceTests
{
    [Fact]
    public void Exact_owner_amounts_are_never_rounded_to_fit_an_accounting_profile()
    {
        Assert.False(AccountingInventoryCostAmountsV1.TryExactDecimal(new("1", "3"), 28, out _));
        Assert.False(AccountingInventoryCostAmountsV1.TryExactDecimal(new("10001", "1000"), 2, out _));
        Assert.True(AccountingInventoryCostAmountsV1.TryExactDecimal(new("-10001", "100"), 2, out var signed));
        Assert.Equal(-100.01m, signed);
        Assert.False(AccountingInventoryCostAmountsV1.TryExactDecimal(new("792281625142643375935439503351", "10"), 1, out _));
        Assert.True(AccountingInventoryCostAmountsV1.TryExactDecimal(AccountingInventoryCostAmountsV1.FromDecimal(decimal.MaxValue), 0, out var maximum));
        Assert.Equal(decimal.MaxValue, maximum);
        Assert.False(AccountingInventoryCostAmountsV1.TryRead(new("01", "1"), out _, out _));
        Assert.False(AccountingInventoryCostAmountsV1.TryRead(new("2", "2"), out _, out _));
    }

    [Fact]
    public void Opposite_late_invoice_residuals_remain_distinct_even_when_the_total_is_zero()
    {
        var (envelope, source) = Purchase();
        AccountingInventoryCostSourceValidationV1.Require(envelope, source, source.CompanyId);
        Assert.Equal(new("0", "1"), AccountingInventoryCostAmountsV1.Add(source.Components[0].SignedTlAmount, source.Components[1].SignedTlAmount));
        Assert.Equal(2, source.Components.Count);
        Assert.NotEqual(source.Components[0].ComponentKey, source.Components[1].ComponentKey);
    }

    [Fact]
    public void A_different_company_or_replaced_source_payload_cannot_reuse_the_received_cost_identity()
    {
        var (envelope, source) = Purchase();
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope, source, Guid.NewGuid()));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope with { PayloadJson = "{\"changed\":true}" }, source, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope, source with { EventId = Guid.NewGuid() }, source.CompanyId));
    }

    [Fact]
    public void A_changed_owner_proof_or_duplicate_component_is_refused_instead_of_hiding_a_second_cost_effect()
    {
        var (envelope, source) = Purchase();
        var component = source.Components[0];
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [component with { SourceProofJson = "{\"changed\":true}" }] }, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [component, component] }, source.CompanyId));
    }

    [Fact]
    public void First_sales_cost_keeps_the_existing_line_specific_posting_trace_and_refuses_purchase_or_negative_components()
    {
        var (envelope, source) = Purchase();
        var line = Guid.NewGuid();
        var component = source.Components[0] with { ComponentKey = AccountingInventoryCostTraceV1.SalesComponent(line),
            InvoiceLineId = line, InvoiceLineNumber = 1, LocationId = null };
        envelope = envelope with { SourceName = AccountingInventoryCostInboxV1.SalesSourceName, EventType = AccountingInventoryCostInboxV1.SalesEventType };
        source = source with { Kind = AccountingInventoryCostKindV1.SalesInvoicedShipment, FinalizedBy = Guid.NewGuid(), Components = [component] };
        AccountingInventoryCostSourceValidationV1.Require(envelope, source, source.CompanyId);
        Assert.Equal(InvoiceLineCogsPostingTraceV1.For(line, source.InvoiceVersion), AccountingInventoryCostTraceV1.SalesPurpose(line, source.InvoiceVersion));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [component with { SignedTlAmount = new("-1", "1") }] }, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [component with { LocationId = Guid.NewGuid() }] }, source.CompanyId));
    }

    private static (WorkInboxEventV1, AccountingInventoryCostSourceV1) Purchase()
    {
        var company = Guid.NewGuid(); var invoice = Guid.NewGuid(); var eventId = Guid.NewGuid(); var product = Guid.NewGuid();
        var envelope = new WorkInboxEventV1(AccountingInventoryCostInboxV1.InventorySourceName, eventId, company,
            AccountingInventoryCostInboxV1.PurchaseDifferenceEventType, 1, "{\"synthetic\":true}", DateTimeOffset.UtcNow, "cost-source", null);
        AccountingInventoryCostComponentV1 Component(Guid location, int amount)
        {
            var proof = JsonSerializer.Serialize(new { product, location, amount });
            return new(AccountingInventoryCostTraceV1.PurchaseComponent(product, location), product, null, null, location,
                new(amount.ToString(CultureInfo.InvariantCulture), "1"), proof, AccountingInventoryCostSourceValidationV1.Sha256(proof));
        }
        return (envelope, new(company, eventId, AccountingInventoryCostKindV1.PurchaseInvoiceDifference, invoice, 2,
            Guid.NewGuid(), new(2026, 11, 17), null, null, AccountingInventoryCostSourceValidationV1.Sha256(envelope.PayloadJson),
            [Component(Guid.NewGuid(), 10), Component(Guid.NewGuid(), -10)]));
    }
}
