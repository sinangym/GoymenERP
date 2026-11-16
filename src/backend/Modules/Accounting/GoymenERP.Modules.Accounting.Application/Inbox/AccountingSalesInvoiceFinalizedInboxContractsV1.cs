using GoymenERP.BuildingBlocks.Messaging;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

// FIN-S13 (B-04; KLN-D6..D19): Accounting listens to the Sales "invoice finalized" event through the Work inbox (dedup per
// company, consumer and event id) and offers the invoice to its own B-04b admission port, which writes ONE draft voucher
// (120 receivable / 600-601 revenue / 391 output VAT, accounts only from the posting account mapping) in Accounting's own
// tables. Accounting never writes a Sales table and never recomputes an amount (KLN-D14, SAT-D223). The consumer is NOT
// registered with the dispatcher yet: AddWorkInboxConsumer validates on start that the source is registered, and Sales does not
// register its outbox as a Work source yet. The dispatcher itself stays off until Work:OutboxDispatcher:Enabled is set (a human
// gate). The purchase side (purchasing.invoice.finalized.v1, 320) has no event and no admission kind yet; it is not consumed.

/// <summary>The Accounting side of <c>sales.invoice.finalized.v1</c> (consumer name, source, event type, version).</summary>
public static class AccountingSalesInvoiceFinalizedInboxV1
{
    /// <summary>The Accounting consumer's registered name (dedup key part).</summary>
    public const string ConsumerName = "accounting.invoice-journal";

    /// <summary>The Sales outbox source name (draft; must equal the name Sales registers with AddWorkOutboxEventSource).</summary>
    public const string SourceName = "sales";

    /// <summary>The finalised sales invoice event (SAT-S1 shape, appended keys only).</summary>
    public const string EventType = "sales.invoice.finalized.v1";

    /// <summary>The one version this consumer reads.</summary>
    public const int Version = 1;

    /// <summary>The consumer's subscriptions, for the composition root's AddWorkInboxConsumer call once the source exists.</summary>
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } =
    [
        new(SourceName, EventType, Version),
    ];

    /// <summary>
    /// The caller identity the consumer presents to the admission port. No user acts: the writer is the inbox consumer (KLN-D10:
    /// the system creates the draft; the finalising user is carried separately as the source actor). It is outside the grantable
    /// permission names; nothing is registered and nothing is granted.
    /// </summary>
    public const string ActorId = "inbox." + ConsumerName;

    /// <summary>The audit row's permission field when a host authority admits the consumer (the consumer, not a grantable name).</summary>
    public const string AuditPermissionCode = "inbox." + ConsumerName;
}

/// <summary>One line of the event, as Sales serialised it (only the keys Accounting reads; other keys are ignored).</summary>
public sealed record AccountingSalesInvoiceFinalizedLineV1(
    Guid LineId,
    int LineNo,
    string? ItemType,
    string? BuyerSituation,
    decimal? VatPercent,
    string? ExemptionCode,
    decimal? Net,
    decimal? Vat,
    decimal? Withholding,
    decimal? FunctionalNet,
    decimal? FunctionalVat,
    decimal? FunctionalWithholding);

/// <summary>
/// The keys of <c>sales.invoice.finalized.v1</c> Accounting reads. <see cref="InvoiceDate"/> (the official invoice date of KLN-D8)
/// and <see cref="FinalizedBy"/> (the finalising user of KLN-D10/D17) are NOT in the SAT-S1 shape yet; they are requested from
/// Sales as appended keys under these draft names. An event without them is not admitted (the period date and the source actor
/// are never guessed: <c>businessDate</c> is the draft's server day, not the invoice date).
/// </summary>
public sealed record AccountingSalesInvoiceFinalizedEventV1(
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
    IReadOnlyList<AccountingSalesInvoiceFinalizedLineV1?>? Lines,
    DateOnly? InvoiceDate,
    string? FinalizedBy);

/// <summary>
/// A delivery Accounting could not answer yet (the port was unavailable or a mapping is still waiting: "eşleme bekliyor"). An
/// ordinary exception, so the dispatcher retries it with backoff; the same key replays the first answer, nothing is created twice.
/// </summary>
public sealed class AccountingInvoiceJournalRetryException(string reason, string message) : Exception(message)
{
    /// <summary>The admission port's stable reason code.</summary>
    public string Reason { get; } = reason;
}
