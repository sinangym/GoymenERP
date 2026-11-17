using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseIncomingInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Receipt_share_prices_are_summed_exactly_then_rounded_once_for_the_invoice_line()
    {
        var invoice = Invoice(); var source = Match(invoice);
        var first = source with { Quantity = 5m, ExpectedNet = CommercialLinePriceCalculator.ExactLineAmount(5m, 10.0004m, null, null, null, null) };
        var second = source with { AcceptanceId = Guid.NewGuid(), Quantity = 5m,
            ExpectedNet = CommercialLinePriceCalculator.ExactLineAmount(5m, 9.9988m, null, null, null, null) };
        invoice.Match(1, [first, second], Policy(), "matcher");
        Assert.Equal(50.002m, invoice.Matches[0].ExpectedNet);
        Assert.Equal(49.994m, invoice.Matches[1].ExpectedNet);
        Assert.Empty(invoice.Variances);
    }

    [Fact]
    public void An_unrepresentable_receipt_share_never_silently_rounds_to_zero()
    {
        Assert.Throws<OverflowException>(() => CommercialLinePriceCalculator.ExactLineAmount(0.000001m,
            0.0000000000000000000000000001m, null, null, null, null));
    }

    [Fact]
    public void Foreign_official_amounts_are_preserved_even_when_the_control_rate_differs()
    {
        var invoice = Invoice(foreign: true);
        var source = Match(invoice) with { ControlRate = 38m };
        invoice.Match(1, [source], Policy(), "matcher");
        var difference = Assert.Single(invoice.Variances);
        Assert.Equal(("ExchangeRate", 38m, 40m), (difference.Kind, difference.Expected, difference.Actual));
        Assert.Equal(PurchaseIncomingInvoice.VarianceApprovalRequired,
            Assert.Throws<PurchasingRuleException>(() => invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now)).Code);
        invoice.ApproveVariance(2, "manager", "SENTETIK official FX approval", Now);
        var admission = Guid.NewGuid();
        invoice.Finalize(3, admission, "finalizer", Now);
        Assert.Equal(40m, invoice.OfficialRate);
        Assert.Equal(new PurchaseInvoiceAmounts(4000m, 800m, 0m), invoice.FunctionalAmounts);
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, invoice.Status);
        Assert.Equal(admission, invoice.AdmissionId);
        Assert.Equal(4, invoice.FinalizedVersion);
        invoice.AcknowledgeFinance(4, admission, Now.AddMinutes(1));
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, invoice.Status);
        Assert.Equal(4, invoice.FinalizedVersion);
        Assert.Equal(5, invoice.Version);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(110)]
    public void Both_favourable_and_unfavourable_price_differences_require_approval(decimal expectedNet)
    {
        var invoice = Invoice();
        invoice.Match(1, [Match(invoice) with { ExpectedNet = expectedNet }], Policy(), "matcher");
        Assert.Equal("Price", Assert.Single(invoice.Variances).Kind);
        var before = invoice.Version;
        Assert.Equal(PurchaseIncomingInvoice.MakerChecker,
            Assert.Throws<PurchasingRuleException>(() => invoice.ApproveVariance(before, "recorder", "SENTETIK", Now)).Code);
        Assert.Equal(PurchaseIncomingInvoice.MakerChecker,
            Assert.Throws<PurchasingRuleException>(() => invoice.ApproveVariance(before, "matcher", "SENTETIK", Now)).Code);
        Assert.Equal(before, invoice.Version);
        invoice.ApproveVariance(before, "manager", "SENTETIK difference accepted", Now);
        invoice.Finalize(invoice.Version, Guid.NewGuid(), "finalizer", Now);
        Assert.Equal(100m, invoice.Amounts.Net);
    }

    [Fact]
    public void An_absent_policy_cannot_be_replaced_by_zero_tolerance_or_by_approval()
    {
        var invoice = Invoice();
        Assert.Equal(PurchaseIncomingInvoice.PolicyUnavailable,
            Assert.Throws<PurchasingRuleException>(() => invoice.Match(1, [Match(invoice)], null, "matcher")).Code);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Empty(invoice.Matches);
        Assert.Equal(1, invoice.Version);
    }

    [Fact]
    public void Every_receipt_keeps_its_own_day_and_control_rate()
    {
        var invoice = Invoice(foreign: true);
        var first = Match(invoice) with { Quantity = 4m, ExpectedNet = 40m, ControlRate = 39m };
        var second = first with { AcceptanceId = Guid.NewGuid(), Quantity = 6m, ExpectedNet = 60m, ControlRate = 38m,
            AcceptedOn = first.AcceptedOn.AddDays(1) };
        invoice.Match(1, [first, second], Policy(), "matcher");
        Assert.Equal(2, invoice.Variances.Count);
        Assert.Equal(new[] { 39m, 38m }, invoice.Variances.Select(item => item.Expected));
        Assert.Equal(new[] { first.AcceptedOn, second.AcceptedOn }, invoice.Matches.Select(item => item.AcceptedOn));
    }

    [Fact]
    public void A_receipt_cannot_hide_a_quantity_difference_inside_the_amount_tolerance()
    {
        var invoice = Invoice();
        invoice.Match(1, [Match(invoice) with { Quantity = 9m, ExpectedNet = 100.01m }], Policy() with { AmountTolerance = 0.01m }, "matcher");
        var difference = Assert.Single(invoice.Variances);
        Assert.Equal(("Quantity", 9m, 10m), (difference.Kind, difference.Expected, difference.Actual));
    }

    [Fact]
    public void Another_company_supplier_currency_or_unit_cannot_enter_the_match()
    {
        var invoice = Invoice(); var original = Match(invoice);
        foreach (var bad in new[] { original with { CompanyId = Guid.NewGuid() }, original with { SupplierPartyId = Guid.NewGuid() },
                     original with { CurrencyId = Guid.NewGuid() }, original with { CompanyAccountId = Guid.NewGuid() }, original with { UnitId = Guid.NewGuid() } })
        {
            Assert.Equal(PurchaseIncomingInvoice.SourceMismatch,
                Assert.Throws<PurchasingRuleException>(() => invoice.Match(1, [bad], Policy(), "matcher")).Code);
            Assert.Empty(invoice.Matches);
            Assert.Equal(1, invoice.Version);
        }
    }

    [Fact]
    public void A_stale_expected_version_and_second_finalization_do_not_allocate_another_admission()
    {
        var invoice = Invoice(); invoice.Match(1, [Match(invoice)], Policy(), "matcher");
        var admission = Guid.NewGuid(); invoice.Finalize(2, admission, "finalizer", Now);
        Assert.Throws<PurchasingVersionConflictException>(() => invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now));
        Assert.Equal(PurchaseIncomingInvoice.StateConflict,
            Assert.Throws<PurchasingRuleException>(() => invoice.Finalize(3, Guid.NewGuid(), "finalizer", Now)).Code);
        Assert.Equal(admission, invoice.AdmissionId);
        Assert.Equal(3, invoice.Version);
        Assert.Equal(PurchaseIncomingInvoice.SourceMismatch,
            Assert.Throws<PurchasingRuleException>(() => invoice.AcknowledgeFinance(3, Guid.NewGuid(), Now)).Code);
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, invoice.Status);
    }

    [Fact]
    public void Imported_amounts_and_installments_must_conserve_each_official_total_without_silent_rounding()
    {
        var good = Invoice();
        Assert.Throws<ArgumentException>(() => Copy(good, lines: [good.Lines[0] with { Amounts = new(99.99m, 20m, 0m) }]));
        Assert.Throws<ArgumentException>(() => Copy(good, installments: [good.Installments[0] with { Amount = 119.99m }]));
        Assert.Throws<ArgumentException>(() => Copy(good, lines: [good.Lines[0] with { Quantity = 10.0000001m }]));
        Assert.Throws<ArgumentException>(() => Copy(good, sourceProof: good.Source with { Sha256 = "not-a-checksum" }));
    }

    [Fact]
    public void Input_arrays_cannot_mutate_the_recorded_or_matched_document()
    {
        var good = Invoice(); var lines = good.Lines.ToArray(); var installments = good.Installments.ToArray();
        var invoice = Copy(good, lines, installments);
        lines[0] = lines[0] with { Amounts = new(0m, 0m, 0m) };
        installments[0] = installments[0] with { Amount = 1m };
        var matches = new[] { Match(invoice) }; invoice.Match(1, matches, Policy(), "matcher");
        matches[0] = matches[0] with { ExpectedNet = 1m };
        Assert.Equal(100m, invoice.Lines[0].Amounts.Net);
        Assert.Equal(120m, invoice.Installments[0].Amount);
        Assert.Equal(100m, invoice.Matches[0].ExpectedNet);
    }

    [Fact]
    public void Snapshot_round_trip_preserves_the_official_source_and_finalization_after_finance_acknowledgement()
    {
        var invoice = Invoice(foreign: true);
        invoice.Match(1, [Match(invoice)], Policy(), "matcher");
        invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now);
        var original = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
        var key = PurchaseIncomingInvoiceFinalizationV1.AdmissionKey(invoice);
        invoice.AcknowledgeFinance(3, invoice.AdmissionId!.Value, Now.AddMinutes(1));
        var json = JsonSerializer.Serialize(PurchaseIncomingInvoiceSnapshot.Capture(invoice));
        var restored = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(json)!.Restore();
        Assert.Equal(invoice.Source, restored.Source);
        Assert.Equal(key, PurchaseIncomingInvoiceFinalizationV1.AdmissionKey(restored));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(PurchaseIncomingInvoiceFinalizationV1.Event(restored)));
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, restored.Status);
    }

    [Fact]
    public void Corrupt_snapshot_cannot_skip_matching_or_variance_approval()
    {
        var invoice = Invoice();
        var snapshot = PurchaseIncomingInvoiceSnapshot.Capture(invoice);
        Assert.Throws<InvalidOperationException>(() => (snapshot with { Version = 2 }).Restore());
        Assert.Throws<InvalidOperationException>(() => (snapshot with { Status = PurchaseIncomingInvoiceStatus.Matched }).Restore());
        invoice.Match(1, [Match(invoice) with { ExpectedNet = 99m }], Policy(), "matcher");
        invoice.ApproveVariance(2, "manager", "SENTETIK", Now);
        invoice.Finalize(3, Guid.NewGuid(), "finalizer", Now);
        var finalized = PurchaseIncomingInvoiceSnapshot.Capture(invoice);
        Assert.Throws<PurchasingRuleException>(() => (finalized with { VarianceDecision = null }).Restore());
    }

    [Fact]
    public void An_unfinalized_invoice_cannot_be_published_or_given_a_finance_retry_identity()
    {
        var invoice = Invoice();
        Assert.Throws<InvalidOperationException>(() => PurchaseIncomingInvoiceFinalizationV1.Event(invoice));
        Assert.Throws<InvalidOperationException>(() => PurchaseIncomingInvoiceFinalizationV1.AdmissionKey(invoice));
    }

    private static PurchaseInvoiceVariancePolicy Policy() => new(Guid.NewGuid(), 1, 0m, 0m);

    [Fact]
    public void Finance_offer_keeps_the_finalized_payload_when_acknowledged_and_changes_only_the_retry_actor_key()
    {
        var invoice = Invoice(foreign: true);
        invoice.Match(1, [Match(invoice)], Policy(), "matcher");
        invoice.Finalize(2, Guid.NewGuid(), "finalizer", Now);
        var first = PurchaseIncomingInvoiceFinalizationV1.Finance(invoice, "finalizer", "correlation-1", "cause");
        invoice.AcknowledgeFinance(3, invoice.AdmissionId!.Value, Now.AddMinutes(1));
        var replay = PurchaseIncomingInvoiceFinalizationV1.Finance(invoice, "finalizer", "correlation-2", "cause");
        Assert.Equal(first.IdempotencyKey, replay.IdempotencyKey);
        Assert.Equal("3", replay.FinalizedVersion);
        Assert.Equal(first.AdmissionId, replay.AdmissionId);
        Assert.Equal("40", replay.OfficialRate);
        Assert.Equal(first.Installments, replay.Installments);
        Assert.Equal(("120.00", "4800.00"), (replay.Installments[0].Amount, replay.Installments[0].FunctionalAmount));
        var another = PurchaseIncomingInvoiceFinalizationV1.Finance(invoice, "another-finalizer", "correlation-3", null);
        Assert.NotEqual(first.IdempotencyKey, another.IdempotencyKey);
        Assert.True(another.IdempotencyKey.Length <= 128);
        Assert.Equal(first.AdmissionId, another.AdmissionId);
        Assert.Equal(first.Installments, another.Installments);
        Assert.Equal("another-finalizer", another.ActorId);
        Assert.Equal("finalizer", invoice.FinalizedBy);
    }

    [Theory]
    [InlineData("0.4999995")]
    [InlineData("0.5000005")]
    [InlineData("1000000000000000000")]
    public void Receipt_quantity_that_cannot_be_stored_exactly_is_rejected_without_changing_invoice_state(string quantity)
    {
        var invoice = Invoice();
        var value = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => invoice.Match(1, [Match(invoice) with { Quantity = value }], Policy(), "matcher"));
        Assert.Equal(1, invoice.Version);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Empty(invoice.Matches);
    }
    [Fact]
    public void Expense_invoice_identity_is_explicit_and_does_not_change_legacy_goods_or_service_JSON()
    {
        var source = Invoice(); var line = source.Lines[0];
        Assert.DoesNotContain("expenseTypeId", JsonSerializer.Serialize(line, PurchasingJson.Options));
        var type = Guid.NewGuid();
        var expense = line with { ProductId = Guid.Empty, ItemType = "Expense", LinePurpose = "expense", ExpenseTypeId = type };
        Assert.Equal(type, Copy(source, [expense]).Lines[0].ExpenseTypeId);
        Assert.Null(Copy(source, [expense with { ExpenseTypeId = null }]).Lines[0].ExpenseTypeId); // ALS-D107: actual empty catalog.
        Assert.Throws<ArgumentException>(() => Copy(source, [expense with { ProductId = Guid.NewGuid() }]));
        Assert.Throws<ArgumentException>(() => Copy(source, [expense with { ServiceTypeId = Guid.NewGuid() }]));
        Assert.Throws<ArgumentException>(() => Copy(source, [expense with { ItemType = "Service" }]));
        Assert.Throws<ArgumentException>(() => Copy(source, [line with { ExpenseTypeId = type }]));
        var eventLine = new PurchaseInvoiceFinalizedLineV1(line.Id, 1, "Expense", "expense", 20m, 100m, 20m, 0m,
            null, null, null, ExpenseTypeId: type);
        Assert.Contains(type.ToString("D"), JsonSerializer.Serialize(eventLine, PurchasingJson.Options));
        Assert.DoesNotContain("expenseTypeId", JsonSerializer.Serialize(eventLine with { ItemType = "material", LinePurpose = "resale", ExpenseTypeId = null }, PurchasingJson.Options));
    }
    private static PurchaseInvoiceReceiptMatch Match(PurchaseIncomingInvoice invoice) => new(invoice.CompanyId,
        invoice.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, invoice.SupplierPartyId,
        invoice.CompanyAccountId, invoice.CurrencyId, invoice.Lines[0].UnitId, 10m, 100m, invoice.OfficialRate, new(2026, 11, 15));

    private static PurchaseIncomingInvoice Invoice(bool foreign = false)
    {
        var currency = Guid.NewGuid(); var functional = foreign ? Guid.NewGuid() : currency;
        return PurchaseIncomingInvoice.Record(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), currency, functional, 2, 2,
            foreign ? 40m : null, new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-INV-1", Guid.NewGuid(), new(2026, 11, 17)),
            new(100m, 20m, 0m), foreign ? new(4000m, 800m, 0m) : null,
            [new(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 10m, "material", "resale", 20m,
                new(100m, 20m, 0m), foreign ? new(4000m, 800m, 0m) : null)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 17), 120m, foreign ? 4800m : null)], "recorder", Now);
    }

    private static PurchaseIncomingInvoice Copy(PurchaseIncomingInvoice source, PurchaseInvoiceLine[]? lines = null,
        PurchaseInvoiceInstallment[]? installments = null, PurchaseInvoiceSource? sourceProof = null) => PurchaseIncomingInvoice.Record(
        source.Id, source.CompanyId, source.SupplierPartyId, source.CompanyAccountId, source.CurrencyId, source.FunctionalCurrencyId,
        source.MoneyScale, source.FunctionalScale, source.OfficialRate, sourceProof ?? source.Source, source.Amounts, source.FunctionalAmounts,
        lines ?? source.Lines.ToArray(), installments ?? source.Installments.ToArray(), source.RecordedBy, source.RecordedAtUtc);
}
