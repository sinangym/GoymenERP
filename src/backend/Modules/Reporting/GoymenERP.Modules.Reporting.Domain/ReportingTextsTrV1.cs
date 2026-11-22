namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// Turkish user texts that leave the server inside an exported file (column headers, section names). The screen keeps its own
/// text file; these exist only because a downloaded file has no screen. Kept in one place so they can be localized.
/// </summary>
public static class ReportingTextsTrV1
{
    public static IReadOnlyDictionary<string, string> ColumnHeaders { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["labelCode"] = "Etiket",
        ["labelId"] = "Etiket kimliği",
        ["productId"] = "Ürün kimliği",
        ["locationId"] = "Lokasyon kimliği",
        ["heatNumber"] = "Döküm no",
        ["coilNumber"] = "Bobin no",
        ["physicalKg"] = "Fiziksel kg",
        ["entryDate"] = "Giriş tarihi",
        ["ageDays"] = "Yaş (gün)",
        ["bucket"] = "Yaş aralığı",
        ["orderNumber"] = "Sipariş no",
        ["orderId"] = "Sipariş kimliği",
        ["lineNumber"] = "Satır",
        ["customerId"] = "Müşteri kimliği",
        ["unitCode"] = "Birim",
        ["requestedDate"] = "İstenen teslim",
        ["orderedQuantity"] = "Sipariş miktarı",
        ["shippedQuantity"] = "Sevk miktarı",
        ["inFullDate"] = "Tam sevk tarihi",
        ["onTime"] = "Zamanında",
        ["inFull"] = "Tam",
        ["otif"] = "Zamanında ve tam",
        ["step"] = "Adım",
        ["occurredAt"] = "Zaman (UTC)",
        ["quantityKg"] = "Miktar kg",
        ["partyRole"] = "Taraf rolü",
        ["partyCode"] = "Cari kodu",
        ["partyName"] = "Cari unvanı",
        ["sourceKind"] = "Kaynak türü",
        ["sourceId"] = "Kaynak kimliği",
        ["documentDate"] = "Belge tarihi",
        ["documentKind"] = "Belge türü",
        ["documentNumber"] = "Belge no",
        ["counterpartyCompanyId"] = "Karşı grup şirketi",
        ["currencyCode"] = "Para birimi",
        ["netAmount"] = "Net tutar",
        ["accountId"] = "Hesap kimliği",
        ["currencyId"] = "Para birimi kimliği",
        ["debit"] = "Borç",
        ["credit"] = "Alacak",
        ["balanceSide"] = "Bakiye yönü",
        ["balance"] = "Bakiye",
    };

    public const string FlagTrue = "Evet";
    public const string FlagFalse = "Hayır";

    public static string Header(string columnId) => ColumnHeaders.TryGetValue(columnId, out var text) ? text : columnId;
}
