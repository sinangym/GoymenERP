using GoymenERP.Modules.Purchasing.Domain.Orders;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Domain.Services;

public enum PurchaseServiceOrderKind { ServicePurchase = 1, OwnMaterialSubcontract = 2 }
public enum PurchaseServiceOrderStatus { Draft = 1, Approved = 2, Cancelled = 3 }
public sealed record PurchaseServiceLine(Guid LineId, Guid ServiceTypeId, long ServiceTypeVersion, string ServiceName,
    Guid UnitId, string UnitCode, Guid UnitProfileId, long UnitProfileVersion, int UnitScale,
    decimal Quantity, decimal? UnitPrice, decimal AcceptedQuantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseServiceTax? Tax = null);
public sealed record PurchaseServiceTax(bool VatApplies, decimal? VatPercent, string? ExemptionCode,
    bool WithholdingApplies, decimal? WithholdingPercent, string? WithholdingCode, long SettingsVersion);
public sealed record PurchaseServiceMaterial(Guid LabelId, string LabelVersion, Guid ProductId, string ProductVersion, decimal QuantityKg);
public sealed record PurchaseServiceOutput(Guid ProductId, string ProductVersion, decimal ExpectedQuantityKg);
public sealed record PurchaseServiceAcceptance(Guid AcceptanceId, Guid LineId, decimal Quantity, DateOnly AcceptedOn,
    string RecordedBy, string Reason, DateTimeOffset RecordedAtUtc, long OrderVersionAfter);
public sealed record PurchaseServiceMoney(Guid CurrencyId, string CurrencyCode, Guid ProfileId, long ProfileVersion, int Scale, int? UnitPriceScale)
{
    public PurchaseOrderMoney Value() => PurchaseOrderMoney.Of(CurrencyId, CurrencyCode, ProfileId, ProfileVersion, Scale, UnitPriceScale);
}
public sealed record PurchaseServiceOrderState(Guid CompanyId, Guid OrderId, long Version, PurchaseServiceOrderKind Kind,
    PurchaseServiceOrderStatus Status, Guid SupplierPartyId, Guid CompanyAccountId, string PartyVersion, string AccountVersion,
    DateOnly BusinessDate, string PreparedBy, string? ApprovedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    PurchaseServiceMoney Money, IReadOnlyList<PurchaseServiceLine> Lines, IReadOnlyList<PurchaseServiceMaterial> Materials,
    IReadOnlyList<PurchaseServiceOutput> ExpectedOutputs, IReadOnlyList<PurchaseServiceAcceptance> Acceptances, string? DocumentNumber = null,
    int PriceRevision = 0)
{
    public bool IsOpen => Status == PurchaseServiceOrderStatus.Approved && Lines.Any(x => x.AcceptedQuantity < x.Quantity);
}

/// <summary>ALS-D29/D40, API70–72. Commercial service facts only; neither stock nor ownership is mutated here.</summary>
public static class PurchaseServiceOrder
{
    public const string VersionConflict = "purchasing_service_order_version_conflict";
    public const string StateConflict = "purchasing_service_order_state_conflict";
    public const string SameActor = "purchasing_service_order_same_actor";
    public const string AcceptanceExceeded = "purchasing_service_acceptance_exceeded";

    public static PurchaseServiceOrderState Create(PurchaseServiceOrderState value)
    {
        Validate(value);
        if (value.Version != 1 || value.PriceRevision != 0 || value.Status != PurchaseServiceOrderStatus.Draft || value.ApprovedBy is not null
            || value.Acceptances.Count != 0 || value.Lines.Any(x => x.AcceptedQuantity != 0)) throw new ArgumentException("Yeni hizmet siparişi taslak olmalıdır.");
        return value;
    }

    public static PurchaseServiceOrderState Approve(PurchaseServiceOrderState value, long expected, string actor, DateTimeOffset now)
    {
        RequireVersion(value, expected); RequireActor(actor, now);
        if (value.Status != PurchaseServiceOrderStatus.Draft) throw new PurchaseServiceOrderException(StateConflict);
        if (value.PreparedBy == actor) throw new PurchaseServiceOrderException(SameActor);
        if (value.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract && value.Lines.Any(x => x.Tax is null))
            throw new ArgumentException("Alış fason vergi kaynağı eksik.");
        return value with { Version = checked(value.Version + 1), Status = PurchaseServiceOrderStatus.Approved, ApprovedBy = actor, UpdatedAtUtc = now };
    }

    public static PurchaseServiceOrderState Accept(PurchaseServiceOrderState value, long expected, Guid acceptanceId, Guid lineId,
        decimal quantity, DateOnly acceptedOn, string actor, string reason, DateTimeOffset now)
    {
        RequireVersion(value, expected); RequireActor(actor, now); RequireReason(reason); RequireQuantity(quantity);
        if (value.Status != PurchaseServiceOrderStatus.Approved || acceptanceId == Guid.Empty || acceptedOn < value.BusinessDate)
            throw new PurchaseServiceOrderException(StateConflict);
        if (value.Acceptances.Any(x => x.AcceptanceId == acceptanceId)) throw new PurchaseServiceOrderException(StateConflict);
        var line = value.Lines.SingleOrDefault(x => x.LineId == lineId) ?? throw new ArgumentException("Hizmet satırı bulunamadı.");
        if (decimal.Round(quantity, line.UnitScale) != quantity) throw new ArgumentException("Kabul miktarı birim ölçeğine uymuyor.");
        if (quantity > line.Quantity - line.AcceptedQuantity) throw new PurchaseServiceOrderException(AcceptanceExceeded);
        var result = value with { Version = checked(value.Version + 1), UpdatedAtUtc = now,
            Lines = value.Lines.Select(x => x.LineId == lineId ? x with { AcceptedQuantity = x.AcceptedQuantity + quantity } : x).ToArray(),
            Acceptances = [.. value.Acceptances, new(acceptanceId, lineId, quantity, acceptedOn, actor, reason.Trim(), now, checked(value.Version + 1))] };
        Validate(result); return result;
    }

    /// <summary>ALS-D18/D9: entering a later price creates a new draft revision; performed service and physical references remain historical facts.</summary>
    public static PurchaseServiceOrderState RevisePrices(PurchaseServiceOrderState value, long expected,
        IReadOnlyDictionary<Guid, decimal> prices, string actor, string reason, DateTimeOffset now)
    {
        RequireVersion(value, expected); RequireActor(actor, now); RequireReason(reason);
        ArgumentNullException.ThrowIfNull(prices);
        if (value.Status != PurchaseServiceOrderStatus.Approved) throw new PurchaseServiceOrderException(StateConflict);
        if (prices.Count == 0 || prices.Any(x => value.Lines.All(line => line.LineId != x.Key))) throw new ArgumentException("Fiyat satırı geçersiz.");
        if (prices.All(x => value.Lines.Single(line => line.LineId == x.Key).UnitPrice == x.Value)) throw new PurchaseServiceOrderException(StateConflict);
        var result = value with { Version = checked(value.Version + 1), PriceRevision = checked(value.PriceRevision + 1),
            Status = PurchaseServiceOrderStatus.Draft, PreparedBy = actor, ApprovedBy = null, UpdatedAtUtc = now,
            Lines = value.Lines.Select(line => prices.TryGetValue(line.LineId, out var price) ? line with { UnitPrice = price } : line).ToArray() };
        Validate(result); return result;
    }

    public static void Validate(PurchaseServiceOrderState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.CompanyId == Guid.Empty || value.OrderId == Guid.Empty || value.SupplierPartyId == Guid.Empty
            || value.CompanyAccountId == Guid.Empty || value.BusinessDate == default || value.Version < 1 || value.PriceRevision < 0 || !Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.Status)
            || string.IsNullOrWhiteSpace(value.PartyVersion) || string.IsNullOrWhiteSpace(value.AccountVersion)
            || value.Lines is not { Count: > 0 and <= 100 } || value.Materials is null || value.ExpectedOutputs is null || value.Acceptances is null)
            throw new ArgumentException("Hizmet siparişi kimliği veya kaynağı geçersiz.");
        RequireActor(value.PreparedBy, value.CreatedAtUtc); RequireActor(value.PreparedBy, value.UpdatedAtUtc);
        var money = value.Money.Value();
        if (value.Lines.Select(x => x.LineId).Distinct().Count() != value.Lines.Count) throw new ArgumentException("Hizmet satırı tekrarlı.");
        foreach (var line in value.Lines)
        {
            if (line.LineId == Guid.Empty || line.ServiceTypeId == Guid.Empty || line.ServiceTypeVersion < 1 || line.UnitId == Guid.Empty
                || line.UnitProfileId == Guid.Empty || line.UnitProfileVersion < 1 || line.UnitScale is < 0 or > 6
                || string.IsNullOrWhiteSpace(line.UnitCode) || string.IsNullOrWhiteSpace(line.ServiceName)) throw new ArgumentException("Hizmet satırı kaynağı geçersiz.");
            RequireQuantity(line.Quantity);
            if (decimal.Round(line.Quantity, line.UnitScale) != line.Quantity || line.AcceptedQuantity < 0 || line.AcceptedQuantity > line.Quantity
                || line.UnitPrice is { } p && (p < 0 || p >= 1_000_000_000_000_000_000m || decimal.Round(p, money.PriceScale) != p))
                throw new ArgumentException("Hizmet miktarı veya fiyatı geçersiz.");
            if (line.UnitPrice is { } price) _ = new PurchaseOrderLinePrice(price, line.UnitId).LineAmount(line.Quantity, money);
            if (line.Tax is { } tax && (tax.SettingsVersion < 1
                || tax.VatApplies && tax.VatPercent is not (>= 0 and <= 100)
                || !tax.VatApplies && (tax.VatPercent is not null || string.IsNullOrWhiteSpace(tax.ExemptionCode))
                || tax.WithholdingApplies && (!tax.VatApplies || tax.WithholdingPercent is not (> 0 and <= 100) || string.IsNullOrWhiteSpace(tax.WithholdingCode))
                || !tax.WithholdingApplies && (tax.WithholdingPercent is not null || tax.WithholdingCode is not null)))
                throw new ArgumentException("Alış fason vergi görüntüsü geçersiz.");
            if (value.Acceptances.Where(x => x.LineId == line.LineId).Sum(x => x.Quantity) != line.AcceptedQuantity)
                throw new ArgumentException("Hizmet kabul geçmişi toplamı uyuşmuyor.");
        }
        if (value.Status != PurchaseServiceOrderStatus.Draft && value.Status != PurchaseServiceOrderStatus.Cancelled && string.IsNullOrWhiteSpace(value.ApprovedBy))
            throw new ArgumentException("Onaylayan kaynağı yok.");
        if (value.Status == PurchaseServiceOrderStatus.Approved && value.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract
            && value.Lines.Any(line => line.Tax is null)) throw new ArgumentException("Alış fason vergi kaynağı eksik.");
        if (value.Kind == PurchaseServiceOrderKind.ServicePurchase && (value.Materials.Count != 0 || value.ExpectedOutputs.Count != 0)
            || value.Kind == PurchaseServiceOrderKind.OwnMaterialSubcontract && (value.Materials.Count == 0 || value.ExpectedOutputs.Count == 0))
            throw new ArgumentException("Hizmet ve kendi malı fason kapsamları karıştırılamaz.");
        if (value.Materials.Select(x => x.LabelId).Distinct().Count() != value.Materials.Count) throw new ArgumentException("Malzeme etiketi tekrarlı.");
        foreach (var material in value.Materials)
        {
            if (material.LabelId == Guid.Empty || material.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(material.LabelVersion)
                || string.IsNullOrWhiteSpace(material.ProductVersion)) throw new ArgumentException("Malzeme kaynağı geçersiz.");
            RequireQuantity(material.QuantityKg); if (decimal.Round(material.QuantityKg, 3) != material.QuantityKg) throw new ArgumentException("Kg ölçeği geçersiz.");
        }
        foreach (var output in value.ExpectedOutputs)
        {
            if (output.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(output.ProductVersion)) throw new ArgumentException("Beklenen çıktı kaynağı geçersiz.");
            RequireQuantity(output.ExpectedQuantityKg); if (decimal.Round(output.ExpectedQuantityKg, 3) != output.ExpectedQuantityKg) throw new ArgumentException("Kg ölçeği geçersiz.");
        }
        if (value.Acceptances.Select(x => x.AcceptanceId).Distinct().Count() != value.Acceptances.Count) throw new ArgumentException("Kabul kimliği tekrarlı.");
        foreach (var acceptance in value.Acceptances)
        {
            if (acceptance.AcceptanceId == Guid.Empty || value.Lines.All(x => x.LineId != acceptance.LineId) || acceptance.AcceptedOn < value.BusinessDate
                || acceptance.OrderVersionAfter < 3 || acceptance.OrderVersionAfter > value.Version)
                throw new ArgumentException("Kabul kaynağı geçersiz.");
            RequireQuantity(acceptance.Quantity); RequireActor(acceptance.RecordedBy, acceptance.RecordedAtUtc); RequireReason(acceptance.Reason);
        }
    }

    private static void RequireVersion(PurchaseServiceOrderState value, long expected)
    { Validate(value); if (expected != value.Version) throw new PurchaseServiceOrderException(VersionConflict); }
    private static void RequireActor(string actor, DateTimeOffset now)
    { if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128 || actor != actor.Trim() || now.Offset != TimeSpan.Zero) throw new ArgumentException("Aktör veya UTC zaman geçersiz."); }
    private static void RequireQuantity(decimal quantity)
    { if (quantity <= 0 || quantity >= 1_000_000_000_000_000_000m || decimal.Round(quantity, 6) != quantity) throw new ArgumentException("Miktar geçersiz."); }
    public static string RequireReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 1000 ? reason.Trim() : throw new ArgumentException("Gerekçe zorunludur.");
}
public sealed class PurchaseServiceOrderException(string code) : Exception(code) { public string Code { get; } = code; }
