using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.DocumentGeneration.Pdf;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.Extensions.Localization;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>Prints immutable identity facts only; no stock total, price, formula or copied master value is computed here.</summary>
public sealed class InventoryLabelPdfComposer(IStringLocalizer<InventoryLabelPdfComposer> texts)
{
    public BarcodeLabelPdfRequest Compose(InventoryLabelViewV1 label, DateTimeOffset requestedAtUtc, DocumentPdfBranding? branding = null)
    {
        if (label.LabelId == Guid.Empty || label.CompanyId == Guid.Empty || label.Lot.Id != label.LotId)
            throw new ArgumentException("The label identity does not match its lot.", nameof(label));
        var title = T("Title") + " " + label.Code;
        var lines = new[]
        {
            T("Code") + ": " + label.Code,
            T("Heat") + ": " + (label.HeatNumber ?? T("Unknown")),
            T("Coil") + ": " + (label.CoilNumber ?? T("Unknown")),
            T("LotHeat") + ": " + (label.Lot.HeatNumber ?? T("Unknown")),
            T("LotCoil") + ": " + (label.Lot.CoilNumber ?? T("Unknown")),
        };
        // No active classification, document state, balance or print history enters the replay content.
        var original = JsonSerializer.Serialize(new
        { label.LabelId, label.CompanyId, label.Code, requestedAtUtc, title, lines, InventoryLabelPdfApiV1.TemplateVersion });
        var canonical = branding is null ? original : original + "\n" + JsonSerializer.Serialize(new
        { branding.PublicationId, branding.TemplateVersion, logo = branding.Logo?.Sha256, letterhead = branding.Letterhead?.Sha256 });
        var identity = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return new(new Guid(identity.AsSpan(0, 16)), T("FilePrefix") + "_" + label.Code + ".pdf",
            requestedAtUtc, title, label.Code, lines, branding);
    }

    private string T(string key)
    {
        var value = texts[key];
        return value.ResourceNotFound || string.IsNullOrWhiteSpace(value.Value)
            ? throw new InvalidOperationException("A label PDF resource text is missing.") : value.Value;
    }
}
