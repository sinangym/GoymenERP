using PdfSharp.Fonts;

namespace GoymenERP.DocumentGeneration.Pdf;

internal static class PdfFontResolverRegistration
{
    private static readonly object Sync = new();

    internal static void EnsureRegistered()
    {
        lock (Sync)
        {
            var current = GlobalFontSettings.FontResolver;
            if (ReferenceEquals(current, NotoSansFontResolver.Instance))
            {
                return;
            }

            if (current is not null)
            {
                throw new InvalidOperationException(
                    "Another process-wide PDFsharp font resolver is already registered; this component will not replace it.");
            }

            try
            {
                GlobalFontSettings.FontResolver = NotoSansFontResolver.Instance;
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "The Noto Sans font resolver could not be registered safely in PDFsharp process-wide state.",
                    exception);
            }
        }
    }
}
