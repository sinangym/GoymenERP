using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- SAT-D30 / SAT-D168 / SAT-D213 / SAT-D216: approval of an approval-band over-delivery --------------------------------
//
// SAT-D30 (EK3, verbatim): "Fazla teslim onay eşiği ile blok eşiği arasındaysa satır onay alınana kadar 'onay bekliyor' görünür;
// onay alınınca 'Tamamlandı + onaylı fazla teslim' işareti gösterilir." SAT-D168 (EK24): Satış Uzmanı, Müdür ve Admin onaylar;
// EK28 SAT-D187/D188/D190: "Müdür" = Satış Müdürü, plus Genel Müdür and Admin. SAT-D213 (EK37): "Fazla teslim onayı onay anındaki
// sevk miktarını kapsar; aynı satırda fazlalık sonradan artarsa yeniden onay gerekir." SAT-D216 (EK37): "sevki yapan … kişi kendi
// işlemini onaylayamaz; başkası onaylar."

/// <summary>
/// SAT-D30: POST …/{id}/over-delivery-approval — the expected draft version (text), the line and the SHIPPED total the user saw
/// (exact quantity text of the line's order unit, as the status read showed it). Strict: every member present, no other member.
/// The shipped total is the command's identity of what is approved (SAT-D213): when the delivery owner's total differs at the
/// command, nothing is approved (<see cref="SalesDraftProblemCodesV1.OverDeliveryShippedChanged"/>).
/// </summary>
public sealed record SalesDraftOverDeliveryApprovalRequest(string ExpectedVersion, Guid LineId, string Shipped);

/// <summary>The strict wire reader of the over-delivery approval (the remainder change's shape rules).</summary>
public static class SalesDraftOverDeliveryJson
{
    public static SalesDraftOverDeliveryApprovalRequest ReadApproval(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftOverDeliveryApprovalRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.LineId == Guid.Empty)
            throw new JsonException("Invalid line.");
        if (request.Shipped is null)
            throw new JsonException("Invalid shipped total.");
        SalesDraftJson.RequireDecimalShape(request.Shipped, positive: true);
        return request;
    }
}
