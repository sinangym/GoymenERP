using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>Actual immutable expense proof. NetAmount is a selected monetary share, never a physical receipt or quantity.</summary>
public sealed record PurchaseInvoiceAdditionalCostMatch(Guid CompanyId, Guid InvoiceLineId, Guid CostId, Guid OrderId,
    long SourceOrderVersion, Guid SupplierPartyId, Guid CompanyAccountId, Guid CurrencyId, decimal NetAmount,
    decimal SourceNetAmount, decimal VatPercent, DateOnly CostOn,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ExpenseTypeId = null);
