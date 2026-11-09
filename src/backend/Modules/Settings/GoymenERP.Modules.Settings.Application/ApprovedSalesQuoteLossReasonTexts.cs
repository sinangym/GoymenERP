namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The Turkish display labels of the standard Sales quote loss reasons (SAT-D234 (1), SINAN_KARARLARI_2026-11-09_SATIS_EK46.md),
/// kept apart from the catalog's technical codes. The labels are reviewed source data: a quote stores the label it was lost with
/// as its own snapshot, so changing a text here is a new catalog version, never a silent rewrite.
/// </summary>
public static class ApprovedSalesQuoteLossReasonTexts
{
    public const string PriceTooHigh = "Fiyat yüksek";

    public const string DeliveryNotSuitable = "Termin uygun değil";

    public const string CompetitorChosen = "Rakip seçildi";

    public const string CustomerWithdrew = "Müşteri vazgeçti";

    public const string Other = "Diğer";
}
