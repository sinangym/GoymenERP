namespace GoymenERP.Modules.Documents.Application.Contracts;

/// <summary>STK-S29 / KLN-D33, D73, D109. Closed producer identities, never a role or a grant.</summary>
public enum DocumentTemplateModuleV1 { Sales = 1, Inventory = 2, Logistics = 3 }

public static class DocumentBrandingContractV1
{
    public const string BasePath = "/api/v1/document-branding";
    public const string CompanyOwnerKind = "documents.company-branding";
    public const int MaximumReasonLength = 500;
    public const string SalesTemplate = "sales.order-pdf";
    public const string InventoryTemplate = "inventory.material-label.technical";
    public const string LogisticsTemplate = "logistics.dispatch-note";

    public static string? TemplateCode(DocumentTemplateModuleV1 module) => module switch
    {
        DocumentTemplateModuleV1.Sales => SalesTemplate,
        DocumentTemplateModuleV1.Inventory => InventoryTemplate,
        DocumentTemplateModuleV1.Logistics => LogisticsTemplate,
        _ => null,
    };

    public static bool IsKnown(DocumentTemplateModuleV1 module, string? templateCode) =>
        TemplateCode(module) is { } expected && string.Equals(expected, templateCode, StringComparison.Ordinal);
}

/// <summary>Registration only. Module publishing never confers another module's authority.</summary>
public static class DocumentBrandingPermissionsV1
{
    public const string Manage = "documents.company-branding.manage";
    public const string SalesPrepare = "documents.templates.sales.prepare";
    public const string SalesPublish = "documents.templates.sales.publish";
    public const string InventoryPrepare = "documents.templates.inventory.prepare";
    public const string InventoryPublish = "documents.templates.inventory.publish";
    public const string LogisticsPrepare = "documents.templates.logistics.prepare";
    public const string LogisticsPublish = "documents.templates.logistics.publish";

    public static string? Prepare(DocumentTemplateModuleV1 module) => module switch
    {
        DocumentTemplateModuleV1.Sales => SalesPrepare,
        DocumentTemplateModuleV1.Inventory => InventoryPrepare,
        DocumentTemplateModuleV1.Logistics => LogisticsPrepare,
        _ => null,
    };
    public static string? Publish(DocumentTemplateModuleV1 module) => module switch
    {
        DocumentTemplateModuleV1.Sales => SalesPublish,
        DocumentTemplateModuleV1.Inventory => InventoryPublish,
        DocumentTemplateModuleV1.Logistics => LogisticsPublish,
        _ => null,
    };
    public static IReadOnlyList<string> Names { get; } =
        [Manage, SalesPrepare, SalesPublish, InventoryPrepare, InventoryPublish, LogisticsPrepare, LogisticsPublish];
}

public static class DocumentBrandingCommandsV1
{
    public const string Set = "documents.company-branding.set.v1";
    public const string Prepare = "documents.template.prepare.v1";
    public const string Publish = "documents.template.publish.v1";
    public static bool IsKnown(string value) => value is Set or Prepare or Publish;
}

/// <summary>A fixed vault version, not the mutable current version, a URL or client supplied bytes/hash.</summary>
public sealed record DocumentBrandingAssetReferenceV1(Guid DocumentId, int VersionNo);

/// <summary>Facts from the Documents owner. Only actually uploaded, scanned Clean versions may become assets.</summary>
public sealed record DocumentBrandingAssetProofV1(Guid CompanyId, Guid DocumentId, int VersionNo,
    string ContentType, long SizeBytes, string Sha256, string ScannerId, string UploadedBy, DateTimeOffset UploadedAtUtc);

public sealed record SetCompanyDocumentBrandingV1(string ExpectedVersion,
    DocumentBrandingAssetReferenceV1? Logo, DocumentBrandingAssetReferenceV1? Letterhead, string Reason);

/// <summary>Preparing pins a branding revision and the producer's closed template. No HTML, script or layout input.</summary>
public sealed record PrepareDocumentTemplateV1(DocumentTemplateModuleV1 Module, string TemplateCode,
    string ExpectedDraftVersion, string ExpectedBrandingVersion, string Reason);

public sealed record PublishDocumentTemplateV1(DocumentTemplateModuleV1 Module, string TemplateCode,
    string ExpectedDraftVersion, string ExpectedPublicationVersion, string Reason);

public sealed record CompanyDocumentBrandingV1(Guid CompanyId, string Version,
    DocumentBrandingAssetProofV1? Logo, DocumentBrandingAssetProofV1? Letterhead,
    string PreparedBy, DateTimeOffset PreparedAtUtc, string Reason);

public sealed record DocumentTemplateDraftV1(Guid CompanyId, DocumentTemplateModuleV1 Module, string TemplateCode,
    string Version, string BrandingVersion, DocumentBrandingAssetProofV1? Logo, DocumentBrandingAssetProofV1? Letterhead,
    string PreparedBy, DateTimeOffset PreparedAtUtc, string Reason);

/// <summary>Append-only publication. Later branding edits cannot change these asset or draft references.</summary>
public sealed record DocumentTemplatePublicationV1(Guid PublicationId, Guid CompanyId,
    DocumentTemplateModuleV1 Module, string TemplateCode, string Version, string TemplateVersion,
    string DraftVersion, string BrandingVersion, DocumentBrandingAssetProofV1? Logo,
    DocumentBrandingAssetProofV1? Letterhead, string PreparedBy, string PublishedBy,
    DateTimeOffset PublishedAtUtc, string Reason);

/// <summary>Null means no revision/publication exists, never a read failure.</summary>
public sealed record DocumentBrandingUploadPolicyV1(IReadOnlyList<string> AllowedContentTypes, long MaximumUploadBytes, bool CanUpload);
public sealed record CompanyDocumentBrandingCurrentV1(Guid CompanyId, CompanyDocumentBrandingV1? Branding, DocumentBrandingUploadPolicyV1 UploadPolicy);
public sealed record DocumentTemplateCurrentV1(Guid CompanyId, DocumentTemplateModuleV1 Module, string TemplateCode,
    DocumentTemplateDraftV1? Draft, DocumentTemplatePublicationV1? Publication, CompanyDocumentBrandingV1? CurrentBranding);

public sealed record CompanyDocumentBrandingMutationV1(CompanyDocumentBrandingV1 Branding,
    bool IsReplay, string OriginalCorrelationId);
public sealed record DocumentTemplateDraftMutationV1(DocumentTemplateDraftV1 Draft,
    bool IsReplay, string OriginalCorrelationId);
public sealed record DocumentTemplatePublicationMutationV1(DocumentTemplatePublicationV1 Publication,
    bool IsReplay, string OriginalCorrelationId);

public interface ICompanyDocumentBrandingServiceV1
{
    Task<CompanyDocumentBrandingCurrentV1> GetBrandingAsync(CancellationToken cancellationToken = default);
    Task<DocumentTemplateCurrentV1> GetTemplateAsync(DocumentTemplateModuleV1 module, CancellationToken cancellationToken = default);
    Task<CompanyDocumentBrandingMutationV1> SetBrandingAsync(SetCompanyDocumentBrandingV1 request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<DocumentTemplateDraftMutationV1> PrepareAsync(PrepareDocumentTemplateV1 request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<DocumentTemplatePublicationMutationV1> PublishAsync(PublishDocumentTemplateV1 request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public enum DocumentPublishedTemplateOutcomeV1 { Available = 1, NoSource = 2, Unavailable = 3 }

/// <summary>Owner-read verified bytes of the exact version in a publication. No object key or physical path.</summary>
public sealed record DocumentBrandingAssetContentV1(DocumentBrandingAssetProofV1 Proof, ReadOnlyMemory<byte> Content);

public enum DocumentBrandingAssetSlotV1 { Logo = 1, Letterhead = 2 }
public enum DocumentBrandingAssetRenderOutcomeV1 { Available = 1, Invalid = 2, Unavailable = 3 }
/// <summary>Actual renderer admission of verified uploaded content, not another MIME or business policy.</summary>
public interface IDocumentBrandingAssetRenderValidatorV1
{
    Task<DocumentBrandingAssetRenderOutcomeV1> ValidateAsync(DocumentBrandingAssetContentV1 asset,
        DocumentBrandingAssetSlotV1 slot, CancellationToken cancellationToken = default);
}

public sealed record DocumentPublishedTemplateReadV1(DocumentPublishedTemplateOutcomeV1 Outcome, Guid CompanyId,
    DocumentTemplateModuleV1 Module, string TemplateCode, DocumentTemplatePublicationV1? Publication,
    DocumentBrandingAssetContentV1? Logo, DocumentBrandingAssetContentV1? Letterhead, string? UnavailableReason = null);

/// <summary>
/// Internal producer seam, not a public read grant. The consumer fences its own real owner permission before/after;
/// the reader checks the live context company and fixed uploaded Clean version/hash. Only genuinely absent publication
/// is NoSource. Corrupt, foreign, missing or unavailable proof/storage is Unavailable and must never select a fallback.
/// </summary>
public interface IDocumentPublishedTemplateReadPortV1
{
    Task<DocumentPublishedTemplateReadV1> ReadAsync(Guid companyId, DocumentTemplateModuleV1 module, string templateCode,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentBrandingServiceException(int status, string code, bool isReplay = false,
    string? originalCorrelationId = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}
