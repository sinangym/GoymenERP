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
    public void A_first_receipt_component_cannot_net_against_a_late_difference_for_the_same_product_and_location()
    {
        var (envelope, source) = Purchase(); var late = source.Components[0];
        var currency = Guid.NewGuid();
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(new("1", "3"), currency, 2, out var value, out var rounding));
        var first = late with { ComponentKey = AccountingInventoryCostTraceV1.PurchaseFirstComponent(Guid.NewGuid(), Guid.NewGuid()),
            SignedTlAmount = AccountingInventoryCostAmountsV1.FromDecimal(value),
            RecordedRounding = new(currency, Guid.NewGuid(), 1, 2, rounding!) };
        AccountingInventoryCostSourceValidationV1.Require(envelope, source with { Components = [late, first] }, source.CompanyId);
        Assert.NotEqual(late.ComponentKey, first.ComponentKey);
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [first with { ComponentKey = first.ComponentKey.ToUpperInvariant() }] }, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [first with { RecordedRounding = null }] }, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [late with { RecordedRounding = first.RecordedRounding }] }, source.CompanyId));
    }

    [Fact]
    public void A_first_receipt_rounded_zero_keeps_its_positive_exact_source_but_cannot_hide_a_changed_residue()
    {
        var (envelope, source) = Purchase(); var currency = Guid.NewGuid();
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(new("1", "250"), currency, 2, out _, out var rounding));
        var first = source.Components[0] with { ComponentKey = AccountingInventoryCostTraceV1.PurchaseFirstComponent(Guid.NewGuid(), Guid.NewGuid()),
            SignedTlAmount = new("0", "1"), RecordedRounding = new(currency, Guid.NewGuid(), 1, 2, rounding!) };
        AccountingInventoryCostSourceValidationV1.Require(envelope, source with { Components = [first] }, source.CompanyId);
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [first with { RecordedRounding = first.RecordedRounding with {
                Rounding = rounding! with { ExactSignedResidue = new("0", "1") } } }] }, source.CompanyId));
    }

    [Fact]
    public void First_cost_uses_one_invoice_line_component_without_a_fabricated_location_or_duplicate_legacy_receipt()
    {
        var (envelope, source) = Purchase(); var currency = Guid.NewGuid(); var line = Guid.NewGuid();
        var exact = AccountingInventoryCostAmountsV1.Add(new("1", "200"), new("1", "200"));
        Assert.True(AccountingInventoryCostRoundingV1Rules.TryRound(exact, currency, 2, out var amount, out var rounding));
        Assert.Equal(0.01m, amount);
        var aggregate = source.Components[0] with { ComponentKey = AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(line),
            LocationId = null, SignedTlAmount = AccountingInventoryCostAmountsV1.FromDecimal(amount),
            RecordedRounding = new(currency, Guid.NewGuid(), 1, 2, rounding!) };
        AccountingInventoryCostSourceValidationV1.Require(envelope, source with { Components = [aggregate] }, source.CompanyId);
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [aggregate with { LocationId = Guid.NewGuid() }] }, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [aggregate with { ComponentKey = aggregate.ComponentKey.ToUpperInvariant() }] }, source.CompanyId));
        var legacy = aggregate with { ComponentKey = AccountingInventoryCostTraceV1.PurchaseFirstComponent(Guid.NewGuid(), line),
            LocationId = source.Components[0].LocationId };
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [aggregate, legacy] }, source.CompanyId));
        Assert.Throws<OutboxPermanentFailureException>(() => AccountingInventoryCostSourceValidationV1.Require(envelope,
            source with { Components = [legacy, aggregate] }, source.CompanyId));
        AccountingInventoryCostSourceValidationV1.Require(envelope, source with { Components = [legacy] }, source.CompanyId);
        Assert.Throws<ArgumentException>(() => AccountingInventoryCostTraceV1.PurchaseFirstLineComponent(Guid.Empty));
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
