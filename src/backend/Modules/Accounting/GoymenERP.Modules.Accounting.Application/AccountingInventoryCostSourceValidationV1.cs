using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Lossless transport of an owner's exact amount; no accounting allocation or rounding rule lives here.</summary>
public static class AccountingInventoryCostAmountsV1
{
    public static bool TryRead(AccountingInventoryCostRatioV1? value, out BigInteger numerator, out BigInteger denominator)
    {
        numerator = denominator = default;
        return value is not null && CanonicalInteger(value.Numerator, true, out numerator)
            && CanonicalInteger(value.Denominator, false, out denominator) && denominator > 0
            && BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator) == BigInteger.One;
    }

    public static AccountingInventoryCostRatioV1 FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var numerator = (new BigInteger((uint)bits[2]) << 64) + (new BigInteger((uint)bits[1]) << 32) + (uint)bits[0];
        if ((bits[3] & int.MinValue) != 0) numerator = -numerator;
        return Create(numerator, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }

    public static AccountingInventoryCostRatioV1 Add(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right)
    {
        if (!TryRead(left, out var ln, out var ld) || !TryRead(right, out var rn, out var rd))
            throw new ArgumentException("An exact owner amount is unreadable.");
        return Create(ln * rd + rn * ld, ld * rd);
    }

    /// <summary>Extends a recorded exact unit-cost stamp by its own physical quantity, never commercial invoice kg.</summary>
    public static AccountingInventoryCostRatioV1 Multiply(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right)
    {
        if (!TryRead(left, out var ln, out var ld) || !TryRead(right, out var rn, out var rd))
            throw new ArgumentException("An exact owner amount is unreadable.");
        return Create(ln * rn, ld * rd);
    }

    public static bool TryExactDecimal(AccountingInventoryCostRatioV1 value, int scale, out decimal amount)
    {
        amount = default;
        if (scale is < 0 or > Money.MaximumScale || !TryRead(value, out var numerator, out var denominator)) return false;
        // DivRem proves representability before Decimal is involved. Decimal division could silently round 1/3.
        var units = BigInteger.DivRem(numerator * BigInteger.Pow(10, scale), denominator, out var remainder);
        if (remainder != 0) return false;
        var digits = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var text = (units.Sign < 0 ? "-" : "") + (scale == 0 ? digits : digits[..^scale] + "." + digits[^scale..]);
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount)) return false;
        // Decimal.TryParse may round a too-long mantissa; the round trip must still equal the owner ratio exactly.
        return FromDecimal(amount) == value;
    }

    private static AccountingInventoryCostRatioV1 Create(BigInteger numerator, BigInteger denominator)
    {
        var common = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        return new((numerator / common).ToString(CultureInfo.InvariantCulture), (denominator / common).ToString(CultureInfo.InvariantCulture));
    }
    private static bool CanonicalInteger(string? text, bool signed, out BigInteger value)
    {
        value = default;
        return text is { Length: > 0 and <= 1024 }
            && BigInteger.TryParse(text, signed ? NumberStyles.AllowLeadingSign : NumberStyles.None,
                CultureInfo.InvariantCulture, out value) && value.ToString(CultureInfo.InvariantCulture) == text;
    }
}

public static class AccountingInventoryCostSourceValidationV1
{
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static void Require(WorkInboxEventV1 envelope, AccountingInventoryCostSourceV1 source, Guid companyId)
    {
        RequireEnvelope(envelope, companyId);
        var expectedKind = envelope.SourceName == AccountingInventoryCostInboxV1.SalesSourceName
            ? AccountingInventoryCostKindV1.SalesInvoicedShipment : AccountingInventoryCostKindV1.PurchaseInvoiceDifference;
        if (source.CompanyId != companyId || source.EventId != envelope.EventId || source.Kind != expectedKind
            || source.InvoiceId == Guid.Empty || source.InvoiceVersion < 1 || source.AdmissionId == Guid.Empty
            || source.InvoiceDate == default || source.SourcePayloadSha256 != Sha256(envelope.PayloadJson)
            || source.OfficialInvoiceNumber is not null && !Text(source.OfficialInvoiceNumber, 128)
            || source.FinalizedBy == Guid.Empty || expectedKind == AccountingInventoryCostKindV1.SalesInvoicedShipment && source.FinalizedBy is null
            || source.Components is null || source.Components.Count > 10000
            || expectedKind == AccountingInventoryCostKindV1.PurchaseInvoiceDifference && source.Components.Count == 0)
            throw Invalid("inventory_cost_source_invalid");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var lineNumbers = new HashSet<int>();
        var firstAggregateLines = new HashSet<Guid>();
        var firstLegacyLines = new HashSet<Guid>();
        foreach (var component in source.Components)
        {
            if (component is null || component.ProductId == Guid.Empty || !identities.Add(component.ComponentKey)
                || !AccountingInventoryCostAmountsV1.TryRead(component.SignedTlAmount, out var amount, out _)
                || !Text(component.SourceProofJson, 1_048_576)
                || component.SourceProofSha256 != Sha256(component.SourceProofJson)) throw Invalid("inventory_cost_component_invalid");
            try { using var proof = JsonDocument.Parse(component.SourceProofJson); if (proof.RootElement.ValueKind != JsonValueKind.Object) throw Invalid("inventory_cost_proof_invalid"); }
            catch (JsonException) { throw Invalid("inventory_cost_proof_invalid"); }
            if (expectedKind == AccountingInventoryCostKindV1.SalesInvoicedShipment)
            {
                if (component.RecordedRounding is not null) throw Invalid("inventory_cost_rounding_invalid");
                if (component.InvoiceLineId is null || component.InvoiceLineId == Guid.Empty) throw Invalid("inventory_cost_line_missing");
                if (component.InvoiceLineNumber is null or < 1 || !lineNumbers.Add(component.InvoiceLineNumber.Value)
                    || component.LocationId is not null || amount < 0
                    || component.ComponentKey != AccountingInventoryCostTraceV1.SalesComponent(component.InvoiceLineId.Value))
                    throw Invalid("inventory_cost_line_invalid");
                if (component.AlreadyPostedComponents is { } posted)
                {
                    if (component.SalesPhysicalClaims is not { Count: > 0 } physical || posted.Count is < 1 or > 10000
                        || posted.Select(x => (x.SourceEventId, x.OutputId, x.Shipment.MovementId, x.Shipment.MovementLineNumber)).Distinct().Count() != posted.Count
                        || posted.Any(x => x.CompanyId != companyId || x.SourceEventId == Guid.Empty || x.OutputId == Guid.Empty
                            || x.JournalId == Guid.Empty || x.CurrencyId == Guid.Empty || x.CogsAccountId == Guid.Empty
                            || x.CounterAccountId == Guid.Empty || x.CogsAccountId == x.CounterAccountId
                            || x.Fingerprint != Sha256(JsonSerializer.Serialize(x with { Fingerprint = "" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                            || !physical.Any(p => p.MovementId == x.Shipment.MovementId && p.MovementLineNumber == x.Shipment.MovementLineNumber
                                && p.ShipmentId == x.Shipment.ShipmentId && p.ProductId == x.Shipment.ProductId && p.QuantityKg == x.Shipment.PhysicalQuantityKg)))
                        throw Invalid("inventory_cost_attribution_invalid");
                    var sum = AccountingInventoryCostAmountsV1.FromDecimal(0);
                    foreach (var share in posted)
                    {
                        if (!decimal.TryParse(share.SignedAmount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out var signed)) throw Invalid("inventory_cost_attribution_invalid");
                        sum = AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(signed));
                    }
                    if (component.UnpostedTlAmount is { } unposted)
                    {
                        if (!AccountingInventoryCostAmountsV1.TryRead(unposted, out var remaining, out _) || remaining < 0
                            || !physical.Any(p => !posted.Any(x => x.Shipment.MovementId == p.MovementId
                                && x.Shipment.MovementLineNumber == p.MovementLineNumber)))
                            throw Invalid("inventory_cost_attribution_invalid");
                        sum = AccountingInventoryCostAmountsV1.Add(sum, unposted);
                    }
                    else if (physical.Any(p => !posted.Any(x => x.Shipment.MovementId == p.MovementId
                        && x.Shipment.MovementLineNumber == p.MovementLineNumber))) throw Invalid("inventory_cost_attribution_invalid");
                    if (sum != component.SignedTlAmount) throw Invalid("inventory_cost_attribution_invalid");
                }
                else if (component.UnpostedTlAmount is not null) throw Invalid("inventory_cost_attribution_invalid");
            }
            else
            {
                var aggregate = AccountingInventoryCostTraceV1.IsPurchaseFirstLineComponent(component.ComponentKey);
                var first = aggregate || AccountingInventoryCostTraceV1.IsPurchaseFirstComponent(component.ComponentKey);
                if (component.InvoiceLineId is not null || component.InvoiceLineNumber is not null
                    || (aggregate ? component.LocationId is not null : component.LocationId is null || component.LocationId == Guid.Empty)
                    || !first && (amount == 0
                        || component.ComponentKey != AccountingInventoryCostTraceV1.PurchaseComponent(component.ProductId, component.LocationId!.Value)))
                    throw Invalid("inventory_cost_position_invalid");
                if (first)
                {
                    var line = Guid.Parse(component.ComponentKey.Split('/')[aggregate ? 1 : 2]);
                    // A line aggregate and its old individual receipts must never both create economic components.
                    if (aggregate ? firstLegacyLines.Contains(line) : firstAggregateLines.Contains(line))
                        throw Invalid("inventory_cost_component_invalid");
                    (aggregate ? firstAggregateLines : firstLegacyLines).Add(line);
                }
                if (component.SalesPhysicalClaims is not null || component.AlreadyPostedComponents is not null || component.UnpostedTlAmount is not null)
                    throw Invalid("inventory_cost_attribution_invalid");
                if (first ? component.RecordedRounding is not { } recorded || amount < 0
                        || !AccountingInventoryCostRoundingV1Rules.IsValidRecorded(component.SignedTlAmount, recorded)
                        || !AccountingInventoryCostAmountsV1.TryRead(recorded.Rounding.ExactSignedAmount, out var original, out _) || original <= 0
                    : component.RecordedRounding is not null) throw Invalid("inventory_cost_rounding_invalid");
            }
        }
    }

    public static void RequireEnvelope(WorkInboxEventV1 envelope, Guid companyId)
    {
        if (companyId == Guid.Empty || envelope.CompanyId != companyId) throw Invalid("company_context_mismatch");
        if (envelope.Version != 1 || !((envelope.SourceName == AccountingInventoryCostInboxV1.SalesSourceName
                && envelope.EventType == AccountingInventoryCostInboxV1.SalesEventType)
            || (envelope.SourceName == AccountingInventoryCostInboxV1.InventorySourceName
                && envelope.EventType == AccountingInventoryCostInboxV1.PurchaseDifferenceEventType))) throw Invalid("unsupported_event");
        if (envelope.EventId == Guid.Empty || envelope.OccurredAtUtc == default || envelope.OccurredAtUtc.Offset != TimeSpan.Zero
            || !Text(envelope.CorrelationId, 128) || string.IsNullOrWhiteSpace(envelope.PayloadJson)) throw Invalid("invalid_envelope");
    }
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    public static OutboxPermanentFailureException Invalid(string code) => new(code, "SMM önerisinin gerçek kaynak kanıtı doğrulanamadı.");
}
