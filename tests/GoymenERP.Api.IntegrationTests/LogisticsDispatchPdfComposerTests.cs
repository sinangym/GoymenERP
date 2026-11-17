using System.Globalization;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.IntegrationTests;

public sealed class LogisticsDispatchPdfComposerTests
{
    [Fact]
    public void The_closed_dispatch_document_contains_actual_product_names_quantities_and_frozen_kilograms_but_no_price_tax_or_money_field()
    {
        var company = TestCompanyIds.CompanyA; var instructionId = Guid.NewGuid(); var noteId = Guid.NewGuid(); var product = Guid.NewGuid(); var unit = Guid.NewGuid(); var shipment = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-11-17T08:00:00Z");
        var instruction = new LoadingInstructionSummaryView(instructionId, company, "Issued", "7", Guid.NewGuid(), Guid.NewGuid(),
            new(2026, 11, 17), at, at, 1, null, null, null, "NotApplicable");
        var note = new DispatchNoteView(noteId, "Approved", "SENTETIK-IRS-1", "SENTETIK-maker", at, "SENTETIK-approver", at,
            null, null, null, null, [new(1, shipment, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), product, unit, "30")]);
        var frozen = DispatchKilogramSnapshot.Theoretical([new(1, shipment, product, 30m)], [new(product, 31m)]);
        var weighing = new WeighingDifferenceViewV1(company, Guid.NewGuid(), noteId, instructionId, 30m, 31m, 1m, "SENTETIK", null, null,
            null, 0m, null, null, "SENTETIK", "Approved", 2, [], [], true, "SENTETIK", at, "SENTETIK-checker", at, frozen);
        var actual = new ProductLogisticsDocumentDisplayV1(ProductLogisticsDocumentDisplayOutcomeV1.Available, company, product, "9", "SENTETIK-01", "SENTETIK İĞÜŞÖÇ");
        var request = new LogisticsDispatchPdfComposer(new Texts()).Compose(instruction, note, "SENTETIK Müşteri",
            new Dictionary<Guid, ProductLogisticsDocumentDisplayV1> { [product] = actual }, new Dictionary<Guid, string> { [unit] = "kg" }, weighing, null);
        var table = Assert.Single(request.Blocks.OfType<GoymenERP.DocumentGeneration.Pdf.DocumentPdfTable>());
        Assert.Equal(new[] { "1", "SENTETIK-01", "SENTETIK İĞÜŞÖÇ", "30", "kg", "31" }, Assert.Single(table.Rows));
        Assert.Equal(new[] { "Line", "ProductCode", "ProductName", "Quantity", "Unit", "FrozenKg" }, table.Columns.Select(x => x.Header));
        Assert.Throws<InvalidOperationException>(() => new LogisticsDispatchPdfComposer(new Texts()).Compose(instruction, note, "SENTETIK",
            new Dictionary<Guid, ProductLogisticsDocumentDisplayV1>(), new Dictionary<Guid, string> { [unit] = "kg" }, weighing, null));
        Assert.Throws<InvalidOperationException>(() => new LogisticsDispatchPdfComposer(new Texts()).Compose(instruction, note, "SENTETIK",
            new Dictionary<Guid, ProductLogisticsDocumentDisplayV1> { [product] = actual }, new Dictionary<Guid, string> { [unit] = "kg" },
            weighing with { CompanyId = TestCompanyIds.CompanyB }, null));
    }
    private sealed class Texts : IStringLocalizer<LogisticsDispatchPdfComposer>
    {
        public LocalizedString this[string name] => new(name, name == "Page" ? "Sayfa {0} / {1}" : name);
        public LocalizedString this[string name, params object[] args] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
