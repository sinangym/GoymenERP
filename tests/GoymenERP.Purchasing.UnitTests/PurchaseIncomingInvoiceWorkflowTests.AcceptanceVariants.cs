using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseIncomingInvoiceWorkflowTests
{
    [Fact]
    public async Task Ordered_variants_forward_the_original_selections_and_empty_costs_to_the_existing_matcher()
    {
        var invoice = Invoice(false, false); var matcher = new VariantMatcher();
        IPurchaseIncomingInvoiceMatchSourceV1 port = matcher;
        var selection = new PurchaseInvoiceOrderedAcceptanceSelectionV1(invoice.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "10");
        var answer = await port.ReadAcceptanceVariantsAsync(invoice, [selection]);
        Assert.Same(matcher.Answer, answer);
        Assert.Same(invoice, matcher.Invoice);
        Assert.Equal(new PurchaseInvoiceReceiptSelectionV1(selection.InvoiceLineId, selection.PurchaseOrderId,
            selection.AcceptanceId, selection.Quantity), Assert.Single(matcher.Selected!));
        Assert.Empty(matcher.Costs!);
        Assert.Equal(1, matcher.Calls);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Equal(1, invoice.Version);
    }

    [Theory]
    [InlineData("unordered")]
    [InlineData("unknown")]
    [InlineData("null")]
    [InlineData("missing-order")]
    [InlineData("quantity")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    public async Task A_nonordered_or_malformed_variant_list_never_partially_calls_the_old_PO_matcher(string defect)
    {
        var invoice = Invoice(false, false); var matcher = new VariantMatcher();
        IPurchaseIncomingInvoiceMatchSourceV1 port = matcher;
        var first = new PurchaseInvoiceOrderedAcceptanceSelectionV1(invoice.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "5");
        PurchaseInvoiceAcceptanceSelectionV1 second = defect switch
        {
            "unordered" => new PurchaseInvoiceUnorderedAcceptanceSelectionV1(first.InvoiceLineId, Guid.NewGuid(), Guid.NewGuid(), "1", "5"),
            "unknown" => new UnknownAcceptanceSelection(first.InvoiceLineId, Guid.NewGuid(), "5"),
            "null" => null!,
            "missing-order" => first with { PurchaseOrderId = Guid.Empty, AcceptanceId = Guid.NewGuid() },
            "quantity" => first with { Quantity = "05", AcceptanceId = Guid.NewGuid() },
            _ => first with { PurchaseOrderId = Guid.NewGuid() },
        };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            port.ReadAcceptanceVariantsAsync(invoice, defect == "empty" ? [] : [first, second]));
        Assert.Equal((503, "service_unavailable"), (refusal.Status, refusal.Code));
        Assert.Equal(0, matcher.Calls);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Null(invoice.AdmissionId);
    }

    [Fact]
    public void The_existing_PO_request_JSON_and_hash_are_unchanged_by_the_separate_variants()
    {
        var invoice = Invoice(false, false); var line = invoice.Lines[0].Id;
        var receipt = new PurchaseInvoiceReceiptSelectionV1(line, Guid.NewGuid(), Guid.NewGuid(), "10");
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1", [receipt]);
        var oldJson = JsonSerializer.Serialize(new { expectedVersion = "1", receipts = new[]
        { new { invoiceLineId = line, purchaseOrderId = receipt.PurchaseOrderId, acceptanceId = receipt.AcceptanceId, quantity = "10" } } }, PurchasingJson.Options);
        Assert.Equal(oldJson, JsonSerializer.Serialize(request, PurchasingJson.Options));
        Assert.Equal(PurchasingRequestHash.Compute(PurchaseIncomingInvoiceCommandsV1.Match, invoice.Id,
            JsonSerializer.Deserialize<PurchaseIncomingInvoiceMatchRequestV1>(oldJson, PurchasingJson.Options)!),
            PurchasingRequestHash.Compute(PurchaseIncomingInvoiceCommandsV1.Match, invoice.Id, request));
    }

    [Fact]
    public async Task Existing_ordered_acceptance_read_is_reused_without_interpreting_order_remaining_as_uninvoiced_stock()
    {
        var invoice = Invoice(false, false);
        var selection = new PurchaseInvoiceOrderedAcceptanceSelectionV1(invoice.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "10");
        var source = new OrderedAcceptanceReader(new(Company, selection.AcceptanceId, selection.PurchaseOrderId,
            Guid.NewGuid(), "3", "10", "0", Now));
        var result = await new PurchaseInvoiceAcceptanceAdmissionV1(source).ReadAsync(invoice, selection, _ => Task.CompletedTask);
        Assert.Equal((selection.PurchaseOrderId, selection.AcceptanceId), source.Request);
        Assert.Same(source.Source, Assert.IsType<PurchaseInvoiceOrderedAcceptanceFactsV1>(result.Facts).Source);
        Assert.Equal(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Found, result.Outcome);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
    }

    [Theory]
    [InlineData("missing-owner", PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Unavailable)]
    [InlineData("not-found", PurchaseInvoiceAcceptanceAdmissionOutcomeV1.NotFound)]
    [InlineData("unavailable", PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Unavailable)]
    [InlineData("contradictory", PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Unavailable)]
    [InlineData("exception", PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Unavailable)]
    public async Task Unordered_source_absence_is_explicit_never_a_fabricated_receipt(string defect,
        PurchaseInvoiceAcceptanceAdmissionOutcomeV1 expected)
    {
        var invoice = Invoice(false, false); var source = new UnorderedAcceptanceReader(UnorderedSource(invoice));
        if (defect == "not-found") source.Answer = new(PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound, null);
        if (defect == "unavailable") source.Answer = new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Unavailable, null);
        if (defect == "contradictory") source.Answer = source.Answer with { Outcome = PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound };
        if (defect == "exception") source.AfterRead = () => throw new InvalidOperationException("SENTETIK owner unavailable");
        var result = await new PurchaseInvoiceAcceptanceAdmissionV1(unordered: defect == "missing-owner" ? null : source)
            .ReadAsync(invoice, UnorderedSelection(invoice, source.Answer.Source ?? UnorderedSource(invoice)), _ => Task.CompletedTask);
        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Facts);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Null(invoice.AdmissionId);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("supplier")]
    [InlineData("account")]
    [InlineData("acceptance")]
    [InlineData("line")]
    [InlineData("unit")]
    [InlineData("product")]
    [InlineData("subject-kind")]
    [InlineData("quantity")]
    [InlineData("scale")]
    [InlineData("available")]
    [InlineData("exhausted")]
    [InlineData("evidence-id")]
    [InlineData("evidence-hash")]
    [InlineData("control-evidence")]
    [InlineData("duplicate-control")]
    [InlineData("time")]
    public async Task Unordered_owner_facts_are_bound_to_the_exact_invoice_without_price_or_quantity_substitutions(string defect)
    {
        var invoice = Invoice(false, false); var original = UnorderedSource(invoice); var selected = UnorderedSelection(invoice, original);
        var evidence = original.Evidence;
        var value = defect switch
        {
            "company" => original with { CompanyId = Guid.NewGuid() },
            "supplier" => original with { SupplierPartyId = Guid.NewGuid() },
            "account" => original with { CompanyAccountId = Guid.NewGuid() },
            "acceptance" => original with { AcceptanceId = Guid.NewGuid() },
            "line" => original with { AcceptanceLineId = Guid.NewGuid() },
            "unit" => original with { UnitId = Guid.NewGuid() },
            "product" => original with { Subject = new PurchaseUnorderedGoodsSubjectV1(Guid.NewGuid(), "1") },
            "subject-kind" => original with { Subject = new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "1") },
            "quantity" => original with { AcceptedQuantity = "010" },
            "scale" => original with { AcceptedQuantity = "10.0000001" },
            "available" => original with { UninvoicedQuantity = "11" },
            "exhausted" => original with { UninvoicedQuantity = "0" },
            "evidence-id" => original with { Evidence = evidence with { DocumentId = Guid.Empty } },
            "evidence-hash" => original with { Evidence = evidence with { Sha256 = "not-a-digest" } },
            "control-evidence" => original with { ControlEvidence = [evidence with { DocumentVersion = 0 }] },
            "duplicate-control" => original with { ControlEvidence = [evidence, evidence] },
            _ => original with { OccurredAtUtc = Now.ToOffset(TimeSpan.FromHours(3)) },
        };
        var result = await new PurchaseInvoiceAcceptanceAdmissionV1(unordered: new UnorderedAcceptanceReader(value))
            .ReadAsync(invoice, selected, _ => Task.CompletedTask);
        Assert.Equal(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Facts);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Empty(invoice.Matches);
        Assert.Null(invoice.AdmissionId);
    }

    [Fact]
    public async Task Valid_operational_facts_without_control_evidence_still_cannot_become_a_PO_match_or_financial_approval()
    {
        var invoice = Invoice(false, false); var original = UnorderedSource(invoice);
        var reader = new UnorderedAcceptanceReader(original); var selected = UnorderedSelection(invoice, original);
        var result = await new PurchaseInvoiceAcceptanceAdmissionV1(unordered: reader).ReadAsync(invoice, selected, _ => Task.CompletedTask);
        Assert.Equal(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Found, result.Outcome);
        var actual = Assert.IsType<PurchaseInvoiceUnorderedAcceptanceFactsV1>(result.Facts).Source;
        Assert.Equal(original with { ControlEvidence = actual.ControlEvidence }, actual);
        Assert.NotSame(original.ControlEvidence, actual.ControlEvidence);
        Assert.Equal(original.ControlEvidence.ToArray(), actual.ControlEvidence.ToArray());
        Assert.Equal((invoice.CompanyId, original.AcceptanceId, original.AcceptanceLineId), reader.Request);
        Assert.Empty(original.ControlEvidence);
        IPurchaseIncomingInvoiceMatchSourceV1 matcher = new VariantMatcher();
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => matcher.ReadAcceptanceVariantsAsync(invoice, [selected]))).Status);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Empty(invoice.Matches);
        Assert.Null(invoice.AdmissionId);
    }

    [Fact]
    public async Task Final_fence_mutation_of_the_owner_list_cannot_replace_validated_control_evidence()
    {
        var invoice = Invoice(false, false); var original = UnorderedSource(invoice);
        var controls = new List<PurchaseAcceptanceDocumentEvidenceV1> { original.Evidence };
        original = original with { ControlEvidence = controls };
        var reader = new UnorderedAcceptanceReader(original); var fences = 0;
        var result = await new PurchaseInvoiceAcceptanceAdmissionV1(unordered: reader)
            .ReadAsync(invoice, UnorderedSelection(invoice, original), _ =>
            {
                if (++fences == 2)
                {
                    controls.Add(original.Evidence with { DocumentVersion = 0 });
                    controls.Add(original.Evidence);
                }
                return Task.CompletedTask;
            });
        Assert.Equal(2, fences);
        Assert.Equal(3, controls.Count);
        Assert.Equal(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Found, result.Outcome);
        var actual = Assert.IsType<PurchaseInvoiceUnorderedAcceptanceFactsV1>(result.Facts).Source;
        Assert.Equal(original with { ControlEvidence = actual.ControlEvidence }, actual);
        Assert.NotSame(controls, actual.ControlEvidence);
        Assert.Equal(original.Evidence, Assert.Single(actual.ControlEvidence));
        Assert.True(Assert.IsAssignableFrom<ICollection<PurchaseAcceptanceDocumentEvidenceV1>>(actual.ControlEvidence).IsReadOnly);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, invoice.Status);
        Assert.Equal(1, invoice.Version);
        Assert.Null(invoice.AdmissionId);
    }

    [Fact]
    public async Task A_service_uses_the_actual_service_identity_and_unit_without_a_product_or_kilogram_substitute()
    {
        var snapshot = PurchaseIncomingInvoiceSnapshot.Capture(Invoice(false, false)); var serviceId = Guid.NewGuid();
        var invoice = (snapshot with { Lines = [snapshot.Lines[0] with
        { ProductId = Guid.Empty, ServiceTypeId = serviceId, ItemType = "Service", LinePurpose = "service" }] }).Restore();
        var source = UnorderedSource(invoice) with { Subject = new PurchaseUnorderedServiceSubjectV1(serviceId, "7") };
        var result = await new PurchaseInvoiceAcceptanceAdmissionV1(unordered: new UnorderedAcceptanceReader(source))
            .ReadAsync(invoice, UnorderedSelection(invoice, source), _ => Task.CompletedTask);
        Assert.Equal(PurchaseInvoiceAcceptanceAdmissionOutcomeV1.Found, result.Outcome);
        Assert.Equal(invoice.Lines[0].UnitId, Assert.IsType<PurchaseInvoiceUnorderedAcceptanceFactsV1>(result.Facts).Source.UnitId);
    }

    [Fact]
    public async Task A_stale_acceptance_version_is_a_conflict_without_matching_or_finalising_the_invoice()
    {
        var invoice = Invoice(false, false); var source = UnorderedSource(invoice);
        var selected = UnorderedSelection(invoice, source) with { ExpectedAcceptanceVersion = "2" };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new PurchaseInvoiceAcceptanceAdmissionV1(unordered: new UnorderedAcceptanceReader(source))
                .ReadAsync(invoice, selected, _ => Task.CompletedTask));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict), (refusal.Status, refusal.Code));
        Assert.Equal(1, invoice.Version);
        Assert.Empty(invoice.Matches);
    }

    [Fact]
    public async Task A_changed_invoice_during_the_source_read_cannot_release_mixed_revision_facts()
    {
        var invoice = Invoice(false, false); var source = UnorderedSource(invoice); var line = invoice.Lines[0];
        var reader = new UnorderedAcceptanceReader(source) { AfterRead = () => invoice.Match(1,
            [new(invoice.CompanyId, line.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
                invoice.SupplierPartyId, invoice.CompanyAccountId, invoice.CurrencyId, line.UnitId, 10m, 100m,
                null, new(2026, 11, 17))], new(Guid.NewGuid(), 1, 0m, 0m), "SENTETIK-other-match") };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new PurchaseInvoiceAcceptanceAdmissionV1(unordered: reader)
                .ReadAsync(invoice, UnorderedSelection(invoice, source), _ => Task.CompletedTask));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict), (refusal.Status, refusal.Code));
        // This read suppresses mixed facts; it does not claim to roll back the separate native transition.
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, invoice.Status);
        Assert.Equal(2, invoice.Version);
        Assert.Null(invoice.AdmissionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_authority_fence_after_success_or_failed_owner_read_prevents_releasing_facts(bool ownerFails)
    {
        var invoice = Invoice(false, false); var source = UnorderedSource(invoice); var allowed = true;
        var reader = new UnorderedAcceptanceReader(source) { AfterRead = () =>
        { allowed = false; if (ownerFails) throw new InvalidOperationException("SENTETIK failed read"); } };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => new PurchaseInvoiceAcceptanceAdmissionV1(unordered: reader)
            .ReadAsync(invoice, UnorderedSelection(invoice, source), _ => allowed ? Task.CompletedTask
                : throw new PurchasingServiceException(403, "forbidden")));
        Assert.Equal(403, refusal.Status);
        Assert.Equal(1, invoice.Version);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_consumer_that_cancels_then_completes_cannot_release_owner_facts(int cancelAt)
    {
        var invoice = Invoice(false, false); var source = UnorderedSource(invoice); var reader = new UnorderedAcceptanceReader(source);
        using var cancellation = new CancellationTokenSource(); var fences = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PurchaseInvoiceAcceptanceAdmissionV1(unordered: reader)
            .ReadAsync(invoice, UnorderedSelection(invoice, source), _ =>
            { if (++fences == cancelAt) cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(cancelAt == 1 ? 0 : 1, reader.Calls);
        Assert.Equal(1, invoice.Version);
    }

    private static PurchaseUnorderedAcceptanceSourceV1 UnorderedSource(PurchaseIncomingInvoice invoice) => new(
        invoice.CompanyId, Guid.NewGuid(), Guid.NewGuid(), 1, invoice.SupplierPartyId, invoice.CompanyAccountId,
        new PurchaseUnorderedGoodsSubjectV1(invoice.Lines[0].ProductId, "1"), invoice.Lines[0].UnitId,
        "10", "10", Now, new(Guid.NewGuid(), 1, new string('a', 64), "actual-acceptance-line"), []);

    private static PurchaseInvoiceUnorderedAcceptanceSelectionV1 UnorderedSelection(PurchaseIncomingInvoice invoice,
        PurchaseUnorderedAcceptanceSourceV1 source) => new(invoice.Lines[0].Id, source.AcceptanceId, source.AcceptanceLineId, "1", "10");

    private sealed record UnknownAcceptanceSelection(Guid InvoiceLineId, Guid AcceptanceId, string Quantity)
        : PurchaseInvoiceAcceptanceSelectionV1(InvoiceLineId, AcceptanceId, Quantity);

    private sealed class OrderedAcceptanceReader(PurchaseAcceptanceSourceV1 source) : IPurchaseAcceptanceSourceV1
    {
        public PurchaseAcceptanceSourceV1 Source => source;
        public (Guid, Guid) Request { get; private set; }
        public Task<PurchaseAcceptanceSourceV1?> ReadAsync(Guid purchaseOrderId, Guid acceptanceId, CancellationToken cancellationToken = default)
        { Request = (purchaseOrderId, acceptanceId); return Task.FromResult<PurchaseAcceptanceSourceV1?>(source); }
    }

    private sealed class UnorderedAcceptanceReader(PurchaseUnorderedAcceptanceSourceV1 source) : IPurchaseUnorderedAcceptanceSourceV1
    {
        public PurchaseUnorderedAcceptanceSourceAnswerV1 Answer { get; set; } = new(PurchaseUnorderedAcceptanceSourceOutcomeV1.Found, source);
        public Action? AfterRead { get; init; }
        public int Calls { get; private set; }
        public (Guid, Guid, Guid) Request { get; private set; }
        public Task<PurchaseUnorderedAcceptanceSourceAnswerV1> ReadAsync(Guid companyId, Guid acceptanceId, Guid lineId,
            CancellationToken cancellationToken = default)
        { Calls++; Request = (companyId, acceptanceId, lineId); AfterRead?.Invoke(); return Task.FromResult(Answer); }
    }

    private sealed class VariantMatcher : IPurchaseIncomingInvoiceMatchSourceV1
    {
        public PurchaseIncomingInvoiceMatchSourceV1 Answer { get; } = new([], new(Guid.NewGuid(), 1, 0m, 0m));
        public int Calls { get; private set; }
        public PurchaseIncomingInvoice? Invoice { get; private set; }
        public IReadOnlyList<PurchaseInvoiceReceiptSelectionV1>? Selected { get; private set; }
        public IReadOnlyList<PurchaseInvoiceAdditionalCostSelectionV1>? Costs { get; private set; }
        public Task<PurchaseIncomingInvoiceMatchSourceV1> ReadAsync(PurchaseIncomingInvoice invoice,
            IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The existing additional-cost dispatch must be used.");
        public Task<PurchaseIncomingInvoiceMatchSourceV1> ReadWithAdditionalCostsAsync(PurchaseIncomingInvoice invoice,
            IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, IReadOnlyList<PurchaseInvoiceAdditionalCostSelectionV1> expenses,
            CancellationToken cancellationToken = default)
        { Calls++; Invoice = invoice; Selected = selected; Costs = expenses; return Task.FromResult(Answer); }
    }
}
