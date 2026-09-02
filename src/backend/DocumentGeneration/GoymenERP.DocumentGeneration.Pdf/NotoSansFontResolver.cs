using PdfSharp.Fonts;

namespace GoymenERP.DocumentGeneration.Pdf;

internal sealed class NotoSansFontResolver : IFontResolver
{
    internal static NotoSansFontResolver Instance { get; } = new();

    private NotoSansFontResolver()
    {
    }

    public byte[]? GetFont(string faceName) =>
        string.Equals(faceName, NotoSansRegularFont.FaceName, StringComparison.Ordinal)
            ? NotoSansRegularFont.GetBytes()
            : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        string.Equals(familyName, NotoSansRegularFont.FamilyName, StringComparison.OrdinalIgnoreCase)
        && !isBold
        && !isItalic
            ? new FontResolverInfo(NotoSansRegularFont.FaceName)
            : null;
}
