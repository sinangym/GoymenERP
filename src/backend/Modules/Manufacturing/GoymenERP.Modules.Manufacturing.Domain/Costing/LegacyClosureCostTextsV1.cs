using System.Globalization;
using System.Resources;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The Turkish user text of an order closed with output above input before URT-D41, read from
/// <c>LegacyClosureCostTextsV1.resx</c> next to this file (AGENTS: user texts are Turkish and come from a resource file).
/// The text is the one of URT-D52 (1) (SINAN_KARARLARI_2026-11-14_URETIM_EK4): "hesaplanamadı — çıkan giren kg'dan
/// fazla". The neutral resources are read with the invariant culture, so the same order gives the same text on every
/// server. A missing text is a build fault, never a key shown to a user: it throws.
/// </summary>
public static class LegacyClosureCostTextsV1
{
    // The resources are found through this type's namespace and name, the manifest name of the .resx next to it.
    private static readonly ResourceManager Resources = new(typeof(LegacyClosureCostTextsV1));

    /// <summary>The text of <see cref="ProductionReceiptCostCodesV1.OutputExceedsInput"/>.</summary>
    public static string OutputExceedsInput => Text(nameof(OutputExceedsInput));

    private static string Text(string name)
    {
        var text = Resources.GetString(name, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text)
            ? throw new InvalidOperationException($"The legacy closure cost text '{name}' is missing.")
            : text;
    }
}
