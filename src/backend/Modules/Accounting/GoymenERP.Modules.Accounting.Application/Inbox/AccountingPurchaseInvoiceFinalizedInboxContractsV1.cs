using GoymenERP.BuildingBlocks.Messaging;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

/// <summary>FIN-S13 / FNS-D105: the versioned Purchasing source consumed by Accounting; no grant or activation.</summary>
public static class AccountingPurchaseInvoiceFinalizedInboxV1
{
    public const string ConsumerName = "accounting.purchase-invoice-journal";
    public const string SourceName = "purchasing";
    public const string EventType = "purchasing.invoice.finalized.v1";
    public const int Version = 1;
    public const string ActorId = "inbox." + ConsumerName;
    public const string AuditPermissionCode = ActorId;

    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } =
    [
        new(SourceName, EventType, Version),
    ];
}

/// <summary>Producer-owned frozen amounts and KLN-D11's explicit purpose; Accounting never reads a Purchasing table.</summary>
public sealed record AccountingPurchaseInvoiceFinalizedLineV1(
    Guid LineId,
    int LineNo,
    string? ItemType,
    string? LinePurpose,
    decimal? VatPercent,
    decimal? Net,
    decimal? Vat,
    decimal? Withholding,
    decimal? FunctionalNet,
    decimal? FunctionalVat,
    decimal? FunctionalWithholding);

/// <summary>AdmissionId is generated once at finalisation; InvoiceDate and FinalizedBy are official source facts.</summary>
public sealed record AccountingPurchaseInvoiceFinalizedEventV1(
    Guid InvoiceId,
    long Version,
    Guid AdmissionId,
    Guid PartyId,
    Guid CurrencyId,
    decimal? Rate,
    decimal? Payable,
    decimal? FunctionalPayable,
    Guid CompanyId,
    decimal? Net,
    decimal? Vat,
    decimal? Withholding,
    decimal? FunctionalNet,
    decimal? FunctionalVat,
    decimal? FunctionalWithholding,
    IReadOnlyList<AccountingPurchaseInvoiceFinalizedLineV1?>? Lines,
    DateOnly? InvoiceDate,
    string? FinalizedBy,
    DateOnly? DueDate = null);
