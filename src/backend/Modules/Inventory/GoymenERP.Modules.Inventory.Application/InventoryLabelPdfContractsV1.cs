using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

public static class InventoryLabelPdfApiV1
{
    public const string Path = "/api/v1/inventory/labels/{labelId:guid}/pdf";
    public const string Command = "inventory.label.pdf.generate.v1";
    public const string TemplateCode = "inventory.material-label.technical";
    public const string TemplateVersion = "1";
    public const string InvalidRequest = "inventory_label_pdf_invalid_request";
    public const string BarcodeUnsupported = "inventory_label_pdf_barcode_unsupported";
}

/// <summary>The client keeps this transport instant with the same key when the result is unknown; no business date is changed.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GenerateInventoryLabelPdfV1([property: JsonRequired] DateTimeOffset RequestedAtUtc);
