namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>
/// <c>POST /api/v1/ai/document-drafts</c> body (WP-B3, DALGA_PLANI_v2 B.3). Exactly one of <see cref="ContentBase64"/>
/// (a file: e-fatura UBL-TR XML, CSV, XLSX, PDF or a scanned image) or <see cref="Text"/> (an e-mail body or pasted
/// text) is given. Everything here is untrusted DATA (AI-D34 c): there is no company, actor, recipient, permission or
/// approval field, and nothing in the content can add one.
/// </summary>
public sealed record AiDocumentDraftRequestV1(
    string? DraftKind,
    string? MediaType,
    string? ContentBase64,
    string? Text,
    string? TextSource);

/// <summary>The three draft kinds of B.3. The owner module creates the record; the draft creates nothing.</summary>
public static class AiDocumentDraftKindsV1
{
    public const string SalesOrder = "sales-order";
    public const string SupplierConfirmation = "supplier-confirmation";
    public const string IncomingInvoice = "incoming-invoice";

    public static IReadOnlyList<string> All { get; } = [SalesOrder, SupplierConfirmation, IncomingInvoice];
}

/// <summary>Accepted media types. Anything else is refused before a byte is read.</summary>
public static class AiDocumentMediaTypesV1
{
    public const string Xml = "application/xml";
    public const string TextXml = "text/xml";
    public const string Csv = "text/csv";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Pdf = "application/pdf";
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string Tiff = "image/tiff";
}

/// <summary>Where a pasted text came from: an e-mail body or any other paste.</summary>
public static class AiDocumentTextSourcesV1
{
    public const string Email = "email";
    public const string Pasted = "pasted";
}

/// <summary>
/// The reading tier that produced the draft (B.3 "kademeli okuma"): 1 structured file fields, 2 locally parsed PDF
/// text, 3 OCR of a scanned image, 4 the local model for fields the first three left open (closed until OPEN-014).
/// Pasted text and e-mail bodies are read by the same deterministic text parser as tier 2.
/// </summary>
public static class AiDocumentReadingTiersV1
{
    public const string StructuredFile = "structured-file";
    public const string PdfText = "pdf-text";
    public const string Ocr = "ocr";
    public const string PastedText = "pasted-text";
    public const string LocalModel = "local-model";
}

/// <summary>A field's verification state. Only <see cref="Verified"/> passed every deterministic check.</summary>
public static class AiDraftFieldStatusesV1
{
    /// <summary>Every deterministic check passed (format, checksum, unit, consistency, owner match where required).</summary>
    public const string Verified = "verified";

    /// <summary>"Doğrulanmadı": present but not proven — not matched, ambiguous, model-suggested or quarantined.</summary>
    public const string Unverified = "unverified";

    /// <summary>A deterministic check failed (wall thickness exceeds the section, unknown unit, bad checksum…).</summary>
    public const string Invalid = "invalid";
}

/// <summary>Stable field codes; the UI owns the Turkish labels.</summary>
public static class AiDraftFieldCodesV1
{
    public const string DocumentNumber = "documentNumber";
    public const string DocumentDate = "documentDate";
    public const string Currency = "currency";
    public const string CounterpartyTaxId = "counterpartyTaxId";
    public const string CounterpartyName = "counterpartyName";
    public const string ProductCode = "productCode";
    public const string Description = "description";
    public const string Quantity = "quantity";
    public const string Unit = "unit";
    public const string UnitPrice = "unitPrice";
    public const string LineAmount = "lineAmount";
    public const string Section = "section";
    public const string WallThickness = "wallThickness";
    public const string Length = "length";
    public const string PayableAmount = "payableAmount";
}

/// <summary>Stable reason codes; never the rejected value.</summary>
public static class AiDraftReasonCodesV1
{
    public const string Missing = "missing";
    public const string QuarantinedInstructionText = "quarantined_instruction_text";
    public const string ModelSuggested = "model_suggested";
    public const string NumberFormatAmbiguous = "number_format_ambiguous";
    public const string NumberInvalid = "number_invalid";
    public const string QuantityNotPositive = "quantity_not_positive";
    public const string PriceNegative = "price_negative";
    public const string DateInvalid = "date_invalid";
    public const string UnitUnknown = "unit_unknown";
    public const string UnitNotOfferedForProduct = "unit_not_offered_for_product";
    public const string CurrencyInvalid = "currency_invalid";
    public const string CurrencyMismatch = "currency_mismatch";
    public const string TaxIdFormatInvalid = "tax_id_format_invalid";
    public const string TaxIdChecksumInvalid = "tax_id_checksum_invalid";
    public const string CounterpartyNotMatched = "counterparty_not_matched";
    public const string CounterpartyMatchUnavailable = "counterparty_match_unavailable";
    public const string ProductNotMatched = "product_not_matched";
    public const string ProductMatchAmbiguous = "product_match_ambiguous";
    public const string ProductMatchUnavailable = "product_match_unavailable";
    public const string SectionFormatInvalid = "section_format_invalid";
    public const string DimensionNotPositive = "dimension_not_positive";
    public const string WallThicknessExceedsSection = "wall_thickness_exceeds_section";
    public const string LengthUnitUnknown = "length_unit_unknown";
    public const string DimensionFormatInvalid = "dimension_format_invalid";
    public const string DimensionConflict = "dimension_conflict";
    public const string LineAmountNotReconciled = "line_amount_not_reconciled";
}

/// <summary>One field of the draft: the value as read, its state, why, and which tier produced it.</summary>
public sealed record AiDraftFieldV1(string Code, string? Value, string Status, string? ReasonCode, string Source);

public sealed record AiDraftCounterpartyV1(AiDraftFieldV1 TaxId, AiDraftFieldV1 Name, Guid? MatchedPartyId);

public sealed record AiDraftLineV1(
    int LineNumber,
    AiDraftFieldV1 ProductCode,
    AiDraftFieldV1 Description,
    AiDraftFieldV1 Quantity,
    AiDraftFieldV1 Unit,
    AiDraftFieldV1 UnitPrice,
    AiDraftFieldV1 LineAmount,
    AiDraftFieldV1 Section,
    AiDraftFieldV1 WallThickness,
    AiDraftFieldV1 Length,
    Guid? MatchedProductId,
    string? MatchedProductVersion);

/// <summary>
/// The stored source document: content hash only; the bytes stay in the object store. AI-D40 retention metadata travels
/// with it: when the owner screen opens a record from the draft, the source is kept as long as that record (YOL-D26);
/// otherwise it becomes passive at <see cref="PassivateAfterUtc"/>. Physical destruction is closed.
/// </summary>
public sealed record AiDraftSourceV1(
    string Sha256,
    string MediaType,
    int ByteLength,
    string RetentionDecision,
    DateTimeOffset StoredAtUtc,
    DateTimeOffset PassivateAfterUtc);

public static class AiDraftRetentionV1
{
    /// <summary>The decision that sets the source retention (AI-D40).</summary>
    public const string Decision = "AI-D40";
}

/// <summary>
/// Where the user continues: the owner module and its creation screen code (the UI maps the code to its route).
/// The draft is handed to that screen; only the owner's own command, with its own checks, creates a record.
/// </summary>
public sealed record AiDraftOpenInV1(string OwnerModule, string ScreenCode);

public sealed record AiDocumentDraftResponseV1(
    string DraftKind,
    string ReadingTier,
    AiDraftSourceV1 Source,
    IReadOnlyList<string> QuarantineFindings,
    AiDraftFieldV1 DocumentNumber,
    AiDraftFieldV1 DocumentDate,
    AiDraftFieldV1 Currency,
    AiDraftCounterpartyV1 Counterparty,
    IReadOnlyList<AiDraftLineV1> Lines,
    AiDraftFieldV1 PayableAmount,
    AiDraftOpenInV1 OpenIn,
    bool CreatesRecord);

public enum AiDocumentDraftStatusV1
{
    Drafted = 1,
    Disabled = 2,
    Unavailable = 3,
    Invalid = 4,
    Refused = 5,
    Unreadable = 6,
}

public sealed record AiDocumentDraftResultV1(
    AiDocumentDraftStatusV1 Status,
    AiDocumentDraftResponseV1? Draft,
    IReadOnlyList<string> InvalidFields)
{
    public static AiDocumentDraftResultV1 Of(AiDocumentDraftStatusV1 status) => new(status, null, []);

    public static AiDocumentDraftResultV1 InvalidInput(params string[] fields) => new(AiDocumentDraftStatusV1.Invalid, null, fields);

    public static AiDocumentDraftResultV1 Drafted(AiDocumentDraftResponseV1 draft) => new(AiDocumentDraftStatusV1.Drafted, draft, []);
}

/// <summary>Stable request field codes for an invalid draft request; never the rejected value.</summary>
public static class AiDocumentDraftInvalidFieldsV1
{
    public const string DraftKind = "draftKind";
    public const string MediaType = "mediaType";
    public const string Content = "content";
    public const string Text = "text";
    public const string TextSource = "textSource";
}
