namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>FNS-D105 / ALS-D59: Purchasing owns the immutable finalisation facts. Accounting consumes this public shape;
/// Purchasing neither references Accounting's implementation nor writes a second payable or journal.</summary>
public static class PurchaseInvoiceFinalizedEventV1
{
    public const string SourceName = "purchasing";
    public const string EventType = "purchasing.invoice.finalized.v1";
    public const int SchemaVersion = 1;
}

/// <summary>Frozen source amounts, including the actual Product owner's type and the explicitly selected line purpose.</summary>
public sealed record PurchaseInvoiceFinalizedLineV1(Guid LineId, int LineNo, string ItemType, string LinePurpose,
    decimal VatPercent, decimal Net, decimal Vat, decimal Withholding,
    decimal? FunctionalNet, decimal? FunctionalVat, decimal? FunctionalWithholding,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ServiceTypeId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ExpenseTypeId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PurchaseInvoiceSubcontractTaxSourceV1>? SubcontractTaxSources = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] PurchaseInvoiceGoodsCostSourceV1? GoodsCostSource = null);

/// <summary>Immutable goods source identities and quantities, never a price distribution or current-stock estimate.</summary>
public sealed record PurchaseInvoiceGoodsCostSourceV1(Guid ProductId, Guid UnitId, decimal InvoiceQuantity,
    IReadOnlyList<PurchaseInvoiceReceiptCostShareV1> Receipts);
public sealed record PurchaseInvoiceReceiptCostShareV1(Guid AcceptanceId, Guid OrderId, Guid OrderLineId,
    long OrderVersion, decimal MatchedQuantity, decimal AcceptedQuantity,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<PurchaseInvoiceReceiptAdjustmentV1>? ReceiptAdjustments = null,
    // AK-3b (ALS-D121): the kilograms weighed for the WHOLE acceptance of a piece-line share, as Purchasing recorded them. The
    // consumer checks them against its own immutable receipt before it applies kg = weighed × matched ÷ accepted pieces.
    // Absent on a kilogram line and on a share recorded before AK-3b, so their payloads are unchanged.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    decimal? MeasuredQuantityKg = null);
public sealed record PurchaseInvoiceReceiptAdjustmentV1(Guid AdjustmentId, decimal UndoQuantity, bool IsSupplierReturn);
public sealed record PurchaseInvoiceOfficialDocumentV1(Guid DocumentId, long DocumentVersion, string Sha256, Guid Ettn);

/// <summary>Approved service-source metadata only. Official invoice amounts are unchanged and never recalculated from it.</summary>
public sealed record PurchaseInvoiceSubcontractTaxSourceV1(Guid OrderId, long OrderVersion, Guid AcceptanceId,
    GoymenERP.Modules.Purchasing.Domain.Services.PurchaseServiceTax Tax);

/// <summary>The admission identity is allocated once with finalisation. Functional values and rate are absent for TL,
/// and use the supplier invoice's own rate for foreign currency (ALS-D41). The consumer does not choose an exchange rate.</summary>
public sealed record PurchaseInvoiceFinalizedPayloadV1(Guid InvoiceId, long Version, Guid AdmissionId, Guid PartyId,
    Guid CurrencyId, decimal? Rate, decimal Payable, decimal? FunctionalPayable, Guid CompanyId,
    decimal Net, decimal Vat, decimal Withholding, decimal? FunctionalNet, decimal? FunctionalVat,
    decimal? FunctionalWithholding, IReadOnlyList<PurchaseInvoiceFinalizedLineV1> Lines,
    DateOnly InvoiceDate, string FinalizedBy, DateOnly? DueDate = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? OfficialInvoiceNo = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] PurchaseInvoiceOfficialDocumentV1? OfficialDocument = null);
