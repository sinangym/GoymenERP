using System.Globalization;
using System.Resources;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The Turkish user texts of the production receipt cost notes, read from <c>ProductionReceiptCostTextsV1.resx</c> next
/// to this file (AGENTS: user texts are Turkish and come from a resource file). The texts are those of the options
/// Sinan chose: "boya maliyeti dahil değil" (SAT-D327 / #66 6857496471 (5)) and "kg dışı girdi maliyeti dahil değil"
/// (URT-D45 / #66 6864631364 (13)). The neutral resources are read with the invariant culture, so the same order gives
/// the same text on every server. A missing text is a build fault, never a key shown to a user: it throws.
/// </summary>
public static class ProductionReceiptCostTextsV1
{
    // The resources are found through this type's namespace and name, the manifest name of the .resx next to it.
    private static readonly ResourceManager Resources = new(typeof(ProductionReceiptCostTextsV1));

    /// <summary>The note of <see cref="ProductionReceiptCostCodesV1.PaintCostNotIncluded"/>.</summary>
    public static string PaintCostNotIncluded => Text(nameof(PaintCostNotIncluded));

    /// <summary>The note of <see cref="ProductionReceiptCostCodesV1.NonKgInputCostNotIncluded"/>.</summary>
    public static string NonKgInputCostNotIncluded => Text(nameof(NonKgInputCostNotIncluded));

    private static string Text(string name)
    {
        var text = Resources.GetString(name, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text)
            ? throw new InvalidOperationException($"The production receipt cost text '{name}' is missing.")
            : text;
    }
}
