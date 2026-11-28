namespace GoymenERP.BuildingBlocks.Work;

/// <summary>Private identity/readiness only. Full owner facts or a genuine original receipt bind the stored payable.</summary>
public sealed record WorkSourceVoucherProofV1(Guid CompanyId, Guid VoucherId, Guid SourceDocumentId,
    Guid InvoiceId, Guid SourceActorId, DateTimeOffset CreatedAtUtc, string DocumentKind, long FinalizedVersion,
    Guid PartyId, Guid CurrencyId, string Status, bool SourceCancelled, bool PeriodOpen,
    string SourceFactsDigest, string PreviewDigest, string? OfficialInvoiceNo);
