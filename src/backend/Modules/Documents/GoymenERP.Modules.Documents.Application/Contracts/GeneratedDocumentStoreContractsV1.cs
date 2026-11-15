namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>
/// F-026 (PLT-S9): the in-process port through which a producing module (Sales order PDF, Inventory delivery/label PDF)
/// stores a PDF it GENERATED into the vault, linked to its own owner record, instead of rendering it and keeping it
/// nowhere. It is the vault's own write path with the fences of an upload — fresh authority of the context actor,
/// the owner's readable answer through its declared port, the PDF allowlist, signature and size check, ADR-011 replay
/// and the same-save audit — plus the DEC-DOC-010 template snapshot. KLN-D59: the authority checked is the PRODUCING
/// command's own permission (<see cref="GeneratedDocumentStoreRequestV1.ProducerPermission"/>, declared by the owner
/// in <see cref="IDocumentOwnerLinkPortV1.GeneratedDocumentProducerPermissions"/>), not <c>documents.files.upload</c>;
/// the audit names the initiating user, the producing command and the document. KLN-D60: the server-generated PDF is
/// not malware-scanned and is stored as <c>SystemGenerated</c> with its SHA-256 (served by the unchanged signed short
/// access); user uploads keep the scan (DEC-DOC-009). A refusal is an outcome, never an exception (cancellation alone propagates), so the
/// caller decides whether a vault refusal blocks its own command; the vault never writes the caller's tables.
/// </summary>
public interface IGeneratedDocumentStorePortV1
{
    Task<GeneratedDocumentStoreResultV1> StoreAsync(GeneratedDocumentStoreRequestV1 request, CancellationToken cancellationToken = default);
}

/// <summary>
/// One generated PDF to keep. <see cref="CompanyId"/> is the caller's own measured company: it must equal the server
/// context company (anything else is <see cref="GeneratedDocumentStoreOutcomeV1.Forbidden"/>), it is never a choice.
/// The owner kind must be one whose owner opted in (<see cref="IDocumentOwnerLinkPortV1.AcceptsGeneratedDocuments"/>);
/// the file name is the Turkish visible name (DEC-DOC-004), independent of any technical id. The idempotency key follows
/// the route's rules (non-blank, canonical, at most 128 characters, no comma); the same key with another PDF, owner,
/// name or template is <see cref="GeneratedDocumentStoreOutcomeV1.IdempotencyConflict"/>.
/// KLN-D59: <see cref="ProducerPermission"/> is the permission the producing command itself already checked; it must be
/// one the owner declared and the context actor must still hold it now, otherwise the outcome is
/// <see cref="GeneratedDocumentStoreOutcomeV1.Forbidden"/> — the port cannot be used outside such a command.
/// <see cref="ProducerCommand"/> is that command's stable technical name (lowercase dotted, e.g. its command constant);
/// it enters the request hash and the audit.
/// </summary>
public sealed record GeneratedDocumentStoreRequestV1(
    Guid CompanyId,
    string? OwnerKind,
    Guid OwnerId,
    string? FileName,
    byte[] Pdf,
    string? TemplateCode,
    string? TemplateVersion,
    string IdempotencyKey,
    string CorrelationId,
    string? CausationId,
    string? ProducerPermission,
    string? ProducerCommand);

public enum GeneratedDocumentStoreOutcomeV1
{
    /// <summary>A new logical document (version 1, the owner link — its primary link) was stored.</summary>
    Stored = 1,

    /// <summary>The same key and request were already stored: the first attempt's document is answered.</summary>
    Replayed = 2,

    /// <summary>The request is malformed (owner, file name, template, metadata) or the bytes are not an accepted PDF.</summary>
    InvalidRequest = 3,

    Unauthenticated = 4,

    /// <summary>
    /// No fresh grant of the producing command's permission, a permission the owner did not declare (KLN-D59), another
    /// company than the context, or the authority changed between the fences.
    /// </summary>
    Forbidden = 5,

    /// <summary>The owner record does not exist in the context company or the actor may not read it (non-disclosure).</summary>
    OwnerNotFound = 6,

    /// <summary>The owner kind did not opt in to generated documents; nothing was written.</summary>
    OwnerNotAccepted = 7,

    /// <summary>Reserved: KLN-D60 stores a generated PDF without a malware scan, so this port no longer answers it.</summary>
    ContentRejected = 8,

    /// <summary>The same idempotency key came with another request or actor (ADR-011).</summary>
    IdempotencyConflict = 9,

    ConcurrentChange = 10,

    /// <summary>The object store, the metadata store or an owner port could not answer; nothing is claimed stored.</summary>
    Unavailable = 11,
}

/// <summary>
/// The port's answer. <see cref="DocumentId"/> is set for <see cref="GeneratedDocumentStoreOutcomeV1.Stored"/> and
/// <see cref="GeneratedDocumentStoreOutcomeV1.Replayed"/>; a newly stored version is <c>SystemGenerated</c> and
/// <see cref="Downloadable"/> (KLN-D60). <see cref="Code"/> is the vault's problem or rule code of a refusal.
/// </summary>
public sealed record GeneratedDocumentStoreResultV1(
    GeneratedDocumentStoreOutcomeV1 Outcome,
    Guid? DocumentId,
    int? VersionNo,
    string? Sha256,
    string? ScanState,
    bool Downloadable,
    string? OriginalCorrelationId,
    string? Code)
{
    public bool IsStored => Outcome is GeneratedDocumentStoreOutcomeV1.Stored or GeneratedDocumentStoreOutcomeV1.Replayed;

    public static GeneratedDocumentStoreResultV1 Refused(GeneratedDocumentStoreOutcomeV1 outcome, string? code) =>
        new(outcome, null, null, null, null, false, null, code);
}

/// <summary>The problem codes the generated-document port adds to <see cref="DocumentProblemCodesV1"/>.</summary>
public static class GeneratedDocumentStoreCodesV1
{
    /// <summary>The owner kind does not accept generated documents (its owner did not opt in).</summary>
    public const string OwnerNotAccepted = "document_generated_owner_not_accepted";

    /// <summary>The template code or version is missing or malformed (DEC-DOC-010 snapshot).</summary>
    public const string TemplateInvalid = "document_generated_template_invalid";

    /// <summary>The generated bytes are not a PDF; this port keeps PDFs only.</summary>
    public const string NotPdf = "document_generated_not_pdf";

    /// <summary>KLN-D59: the producer permission is missing or not declared by the owner of this kind.</summary>
    public const string ProducerNotDeclared = "document_generated_producer_not_declared";

    /// <summary>KLN-D59: the producing command name is missing or malformed.</summary>
    public const string ProducerCommandInvalid = "document_generated_producer_command_invalid";
}
