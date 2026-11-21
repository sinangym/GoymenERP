using System.Globalization;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

public enum ManufacturingNonKgInputKindV1 { Paint = 1, Other = 2 }
public enum ManufacturingNonKgInputUnitV1 { Litre = 1, Metre = 2, Piece = 3 }

/// <summary>
/// URT-D45 / SAT-D327: an actual consumption fact, distinct from PaintingEngine estimates and Inventory kg events.
/// A correction appends a replacement; the original measurement remains visible. No money or kg conversion is implied.
/// </summary>
public sealed record ManufacturingNonKgInputConsumptionV1(Guid FactId, long Revision, ManufacturingNonKgInputKindV1 Kind,
    string Item, ManufacturingNonKgInputUnitV1 Unit, string Quantity, DateTimeOffset OccurredAtUtc,
    Guid? ReplacesFactId, string Reason)
{
    public static string? CanonicalQuantity(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 60 || text.Any(character => character != '.' && !char.IsAsciiDigit(character))
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0) return null;
        var canonical = value.ToString("0.############################", CultureInfo.InvariantCulture);
        return canonical == text ? canonical : null;
    }

    public void RequireValid()
    {
        if (FactId == Guid.Empty || Revision < 1 || !Enum.IsDefined(Kind) || !Enum.IsDefined(Unit)
            || string.IsNullOrWhiteSpace(Item) || Item.Length > 200 || Item != Item.Trim()
            || CanonicalQuantity(Quantity) is null || Quantity == "0" && ReplacesFactId is null
            || Kind == ManufacturingNonKgInputKindV1.Paint && Unit != ManufacturingNonKgInputUnitV1.Litre
            || ReplacesFactId == Guid.Empty || ReplacesFactId == FactId || OccurredAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(Reason) || Reason.Length > 1000 || Reason != Reason.Trim())
            throw new ArgumentException("Gerçek kg dışı sarf kaydı geçersiz.");
    }
}
