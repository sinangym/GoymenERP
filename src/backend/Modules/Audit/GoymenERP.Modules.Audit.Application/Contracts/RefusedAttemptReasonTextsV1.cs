using System.Globalization;
using System.Resources;

namespace GoymenERP.Modules.Audit.Application.Contracts;

/// <summary>
/// The fixed Turkish audit reason of a refused attempt, one per refusal class, read from
/// <c>RefusedAttemptReasonTextsV1.resx</c> next to this file (F-023: "sabit Türkçe metin, resource dosyasından"). The
/// neutral resources are read with the invariant culture so every server writes the same reason. A missing text is a
/// build fault, never a key written to the audit log: it throws.
/// </summary>
public static class RefusedAttemptReasonTextsV1
{
    // The resources are found through this type's namespace and name, the manifest name of the .resx next to it.
    private static readonly ResourceManager Resources = new(typeof(RefusedAttemptReasonTextsV1));

    /// <summary>The reason of <paramref name="refusalClass"/>; an undefined class is refused.</summary>
    public static string For(string refusalClass)
    {
        if (!RefusedAttemptClassesV1.IsDefined(refusalClass))
        {
            throw new ArgumentOutOfRangeException(nameof(refusalClass), refusalClass, "Unknown refusal class.");
        }

        var text = Resources.GetString(refusalClass, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(text)
            ? throw new InvalidOperationException($"The refused attempt reason text '{refusalClass}' is missing.")
            : text;
    }
}
