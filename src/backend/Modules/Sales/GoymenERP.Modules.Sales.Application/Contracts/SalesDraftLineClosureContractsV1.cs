using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- SAT-D16 / SAT-D17 / SAT-D168 / SAT-D214–D216: closing one order line with a shortfall ---------------------------------
//
// SAT-D16 (EK3, verbatim): "Eksik teslim yalnız satır kapatılırken veya son sevk işaretlenince ölçülür; açık kalan miktar eksik
// sayılmaz." SAT-D17: "Uyarı bandında satır doğrudan, onay bandında onaydan sonra 'Tamamlandı' olur; kalan miktar iptal edilen
// kalan sayılır … Blok bandında satır bu yolla kapanmaz." SAT-D168 (EK24): Satış Uzmanı, Müdür ve Admin kapatır. SAT-D214/D215/D216
// (EK37): kullanıcı işlemi, gerekçe yok, kapatılan satıra sevk yapılmaz, sevki yapan kişi kendi satırını kapatamaz.

/// <summary>
/// SAT-D17: POST …/{id}/line-closure — the expected draft version (text), the line and the SHIPPED total the user saw (exact
/// quantity text of the line's order unit, as the status read showed it). Strict: every member present, no other member, and NO
/// reason (SAT-D215). The shipped total is the command's identity of what is closed: when the delivery owner's total differs at
/// the command, nothing is closed (<see cref="SalesDraftProblemCodesV1.LineClosureShippedChanged"/>).
/// </summary>
public sealed record SalesDraftLineClosureRequest(string ExpectedVersion, Guid LineId, string Shipped);

/// <summary>The strict wire reader of the line closure (the over-delivery approval's shape rules).</summary>
public static class SalesDraftLineClosureJson
{
    public static SalesDraftLineClosureRequest Read(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftLineClosureRequest>(json, SalesDraftJson.Options)
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
