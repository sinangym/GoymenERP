using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Domain.Expenses;

public sealed record PurchaseAdditionalCostType(Guid Id, long Version, string Code, string Name);
public sealed record PurchaseAdditionalCostTax(string Code, long ListVersion, DateOnly EffectiveOn, decimal VatPercent);
public enum PurchaseAdditionalCostSourceKind { GoodsOrder = 1, ServiceOrder = 2 }
/// <summary>ALS-D23/D27/D80/D107: a commercial expense fact. No quantity, stock movement, allocation or average-cost input exists.</summary>
public sealed record PurchaseAdditionalCostState(Guid CompanyId, Guid CostId, Guid OrderId, long SourceOrderVersion,
    string? SourceOrderNumber, PurchaseAdditionalCostSourceKind SourceKind, Guid? AcceptanceId, Guid? AcceptanceLineId,
    Guid SupplierPartyId, Guid CompanyAccountId, string PartyVersion, string AccountVersion,
    DateOnly BusinessDate, PurchaseServiceMoney Money, string Description, PurchaseAdditionalCostType? ExpenseType, string TypeSourceFingerprint,
    decimal NetAmount, PurchaseAdditionalCostTax Tax, decimal VatAmount, decimal GrossAmount,
    string RecordedBy, string Reason, DateTimeOffset RecordedAtUtc)
{
    public const long Version = 1; // This fact is immutable; later invoice bindings are separate facts.
}
public static class PurchaseAdditionalCost
{
    public static PurchaseAdditionalCostState Record(PurchaseAdditionalCostState value)
    {
        Validate(value); return value;
    }
    public static (decimal Vat, decimal Gross) Amounts(Guid costId, decimal net, decimal vatPercent, int scale)
    {
        // A lump-sum expense is one financial line, not a fictitious physical unit or receipt.
        var line = CommercialTaxTotalsCalculator.Calculate([new(costId, 1m, new(net), CommercialLineTaxInput.Vat(vatPercent, null))], scale).Lines.Single();
        if (line.NetAmount != net || line.VatAmount is not { } vat || line.GrossAmount is not { } gross)
            throw new ArgumentException("Masraf tutarı kaynak para ölçeğine tam olarak uymalıdır.");
        return (vat, gross);
    }
    public static void Validate(PurchaseAdditionalCostState value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.CompanyId == Guid.Empty || value.CostId == Guid.Empty || value.OrderId == Guid.Empty || value.SourceOrderVersion < 1
            || value.SourceOrderNumber is { Length: > 64 } || !Enum.IsDefined(value.SourceKind)
            || value.AcceptanceId == Guid.Empty || value.AcceptanceLineId == Guid.Empty || (value.AcceptanceId is null) != (value.AcceptanceLineId is null)
            || value.SupplierPartyId == Guid.Empty || value.CompanyAccountId == Guid.Empty
            || string.IsNullOrWhiteSpace(value.PartyVersion) || string.IsNullOrWhiteSpace(value.AccountVersion) || value.BusinessDate == default
            || string.IsNullOrWhiteSpace(value.Description) || value.Description.Trim() != value.Description || value.Description.Length > 1000
            || string.IsNullOrWhiteSpace(value.RecordedBy) || value.RecordedBy.Length > 128 || value.RecordedBy.Trim() != value.RecordedBy
            || value.RecordedAtUtc.Offset != TimeSpan.Zero || value.NetAmount < 0 || value.NetAmount >= 1_000_000_000_000_000_000m)
            throw new ArgumentException("Masraf kaynağı veya açıklaması geçersiz.");
        if (value.TypeSourceFingerprint is null || value.TypeSourceFingerprint.Length != 64 || value.TypeSourceFingerprint.Any(x => !char.IsAsciiHexDigit(x)))
            throw new ArgumentException("Masraf tipi kaynak kanıtı geçersiz.");
        _ = PurchaseServiceOrder.RequireReason(value.Reason);
        var money = value.Money.Value();
        if (decimal.Round(value.NetAmount, money.Scale) != value.NetAmount || value.Tax is null || value.Tax.ListVersion < 1
            || value.Tax.EffectiveOn != value.BusinessDate || string.IsNullOrWhiteSpace(value.Tax.Code) || value.Tax.VatPercent is < 0 or > 100)
            throw new ArgumentException("Masraf vergi veya tutar kaynağı geçersiz.");
        if (value.ExpenseType is { } type && (type.Id == Guid.Empty || type.Version < 1 || string.IsNullOrWhiteSpace(type.Code) || string.IsNullOrWhiteSpace(type.Name)))
            throw new ArgumentException("Masraf tipi görüntüsü geçersiz.");
        var calculated = Amounts(value.CostId, value.NetAmount, value.Tax.VatPercent, money.Scale);
        if (calculated != (value.VatAmount, value.GrossAmount)) throw new ArgumentException("Masraf tutar görüntüsü ortak hesapla uyuşmuyor.");
    }
}
