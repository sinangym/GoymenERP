using System.Globalization;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.LogisticsRuntime;

/// <summary>Closed price-free dispatch document. Stored quantity/unit and frozen owner kilograms are printed verbatim;
/// no formula, kilogram distribution, price, tax or monetary total is calculated by the composer.</summary>
public sealed class LogisticsDispatchPdfComposer(IStringLocalizer<LogisticsDispatchPdfComposer> texts)
{
    public DocumentPdfRequest Compose(LoadingInstructionSummaryView instruction, DispatchNoteView note, string customer,
        IReadOnlyDictionary<Guid, ProductLogisticsDocumentDisplayV1> products, IReadOnlyDictionary<Guid, string> units,
        WeighingDifferenceViewV1? weighing, DocumentPdfBranding? branding)
    {
        if (instruction.CompanyId == Guid.Empty || note.DispatchNoteId == Guid.Empty || note.Status != "Approved"
            || string.IsNullOrWhiteSpace(note.Number) || string.IsNullOrWhiteSpace(customer) || note.Lines.Count == 0)
            throw new InvalidOperationException("The approved dispatch source cannot be printed faithfully.");
        var allocation = weighing?.KilogramAllocation;
        if (weighing is not null && (weighing.CompanyId != instruction.CompanyId || weighing.InstructionId != instruction.Id
            || weighing.DispatchNoteId != note.DispatchNoteId)) throw new InvalidOperationException("The dispatch kilogram source does not match.");
        if (allocation is not null && (allocation.Lines.Count != note.Lines.Count || allocation.Lines.Any(x => !note.Lines.Any(line =>
            line.LineNo == x.LineNo && line.ShipmentId == x.ShipmentId && line.ProductId == x.ProductId))))
            throw new InvalidOperationException("The frozen dispatch kilogram lines do not match the source.");
        var rows = note.Lines.OrderBy(x => x.LineNo).Select(line =>
        {
            if (!products.TryGetValue(line.ProductId, out var product) || product.Outcome != ProductLogisticsDocumentDisplayOutcomeV1.Available
                || product.CompanyId != instruction.CompanyId || product.ProductId != line.ProductId || string.IsNullOrWhiteSpace(product.Code)
                || string.IsNullOrWhiteSpace(product.Name) || !units.TryGetValue(line.UnitId, out var unit) || string.IsNullOrWhiteSpace(unit))
                throw new InvalidOperationException("The dispatch product or unit identity is unavailable.");
            return (IReadOnlyList<string>)new[] { line.LineNo.ToString(CultureInfo.InvariantCulture), product.Code, product.Name,
                line.Quantity, unit, allocation is null ? T("NotRecorded") : allocation.Lines.Single(x => x.ShipmentId == line.ShipmentId && x.LineNo == line.LineNo).Kilograms.ToString(CultureInfo.InvariantCulture) };
        }).ToArray();
        var blocks = new DocumentPdfBlock[]
        {
            new DocumentPdfFields([new(T("Number"), note.Number), new(T("Customer"), customer),
                new(T("EDispatch"), note.EDispatchNumber ?? T("NotRecorded")), new(T("Version"), instruction.Version)]),
            new DocumentPdfTable([new(T("Line"), .5), new(T("ProductCode"), 1.3), new(T("ProductName"), 3),
                new(T("Quantity"), 1.3, DocumentPdfAlignment.End), new(T("Unit"), .7), new(T("FrozenKg"), 1.5, DocumentPdfAlignment.End)], rows),
        };
        if (!int.TryParse(instruction.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new InvalidOperationException("The dispatch version cannot be printed faithfully.");
        return new(Guid.NewGuid(), new(T("FilePrefix"), note.Number, customer, instruction.EffectiveOn, version, note.Status), T("Title"), blocks,
            T("Footer"), T("Page"), Branding: branding);
    }
    private string T(string key)
    {
        var text = texts[key];
        return text.ResourceNotFound || string.IsNullOrWhiteSpace(text.Value)
            ? throw new InvalidOperationException("A dispatch PDF resource is missing.") : text.Value;
    }
}
