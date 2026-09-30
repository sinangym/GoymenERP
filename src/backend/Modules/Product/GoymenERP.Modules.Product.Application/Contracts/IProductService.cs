using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// Every operation requires current server-resolved global authorization, including
/// reference reads and replay. No company-scoped grant satisfies this contract.
/// Product infrastructure implements this port using the accepted platform boundary;
/// no permissive default exists.
/// </summary>
public interface IProductService
{
    Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken);
    Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// §10.2 / §15: the read-only exact-name duplicate candidates for a name being created or edited (a warning to
    /// compare against; it never blocks a save or an approval). Same global Read authority as the list.
    /// </summary>
    Task<ProductDuplicateCandidates> GetDuplicateCandidatesAsync(ProductDuplicateQuery query,
        CancellationToken cancellationToken);
    Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query, CancellationToken cancellationToken);
    Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// DEC-PRD-010: approves a Draft card for firm use (Draft -> Active) at the version the approver
    /// read. Needs the global Product <c>Activate</c> authority; replay and races follow the same
    /// command boundary as the draft writes.
    /// </summary>
    Task<ProductMutationResponse> ActivateAsync(Guid id, ActivateProductRequest request,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// DEC-CAR-003 / PRD-SCOPE-001 (#98) §7.9: takes an Active card out of new use (Active -> Inactive) with a
    /// stated reason, at the version the actor read. Nothing is deleted: the card, its history and every
    /// downstream record that names it stay readable; the operational selection reads refuse it from now on.
    /// Needs the global Product <c>Deactivate</c> authority (PRD-PERM-001 #99: activate/deactivate/reactivate).
    /// </summary>
    Task<ProductMutationResponse> DeactivateAsync(Guid id, ChangeProductLifecycleRequest request,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// PRD-PERM-001 (#99): returns an Inactive card to Active with a stated reason, at the version the actor
    /// read. Needs the global Product <c>Reactivate</c> authority, a grant of its own.
    /// </summary>
    Task<ProductMutationResponse> ReactivateAsync(Guid id, ChangeProductLifecycleRequest request,
        string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>
/// The approval body: only the version the approver read. The card is the route's; an unknown member
/// (a lifecycle, a company, anything else) is refused at binding, as for the draft requests.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ActivateProductRequest([property: JsonRequired] string ExpectedVersion);

/// <summary>
/// The deactivation / reactivation body: the version the actor read and the reason (1..1000 characters after
/// trimming, no control characters, as for a draft edit). The reason is recorded on the canonical audit event
/// and shown in the card history; it is not a card field. An unknown member is refused at binding.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeProductLifecycleRequest(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string Reason);

public sealed record ProductPage(IReadOnlyList<ProductDraftView> Items, string? NextCursor);
public sealed record ProductListQuery(string? Q = null, string? Lifecycle = null,
    Guid? GroupId = null, Guid? ClassId = null, Guid? FamilyId = null,
    string? Roles = null, string RoleMatch = "any", string Sort = "code", string Direction = "asc",
    string? Completeness = null, int PageSize = 50, string? Cursor = null);

public sealed record ProductReferenceQuery(Guid GroupId, Guid? ClassId = null);
public sealed record ProductReferenceOption(Guid Id, string DisplayName);
public sealed record ProductUnitOption(Guid Id, string DisplayName, string Symbol, bool IsKilogram);
/// <summary>
/// A released template and, since PRD-CARD-1, ITS OWN fields: the form renders exactly these, in this order,
/// and no field the release does not state. A field's <see cref="ProductTemplateFieldOption.Dictionary"/> names
/// the entry list in <see cref="ProductReferenceOptions.Dictionaries"/>.
/// </summary>
public sealed record ProductTemplateOption(Guid Id, string Version, string DisplayName,
    IReadOnlyList<ProductTemplateFieldOption>? Fields = null);

/// <summary>
/// One field of a released template as the release states it. <paramref name="Kind"/> is
/// <c>measureMillimetre</c> (a number &gt; 0 with at most three decimals, Ö2) or <c>dictionaryReference</c>
/// (a code of <paramref name="Dictionary"/>). A field with <paramref name="AppliesWhenField"/> applies only
/// while that field holds <paramref name="AppliesWhenCode"/>; a <paramref name="ParentField"/> entry must be
/// the parent of this one. Nothing here is Product's own choice.
/// </summary>
public sealed record ProductTemplateFieldOption(string Key, string Kind, string? Role, bool Required,
    string? Dictionary, bool AllowsCandidate, string? AppliesWhenField, string? AppliesWhenCode, string? ParentField);

/// <summary>One controlled dictionary of the release, with its entries in code order.</summary>
public sealed record ProductDictionaryOption(string Name, IReadOnlyList<ProductDictionaryEntryOption> Entries);

/// <summary>
/// One controlled value. <paramref name="Status"/> is <c>approved</c> or <c>candidate</c>: a candidate is shown
/// as a candidate, carries no invented meaning and never satisfies a required field.
/// <paramref name="IsExplicitNone"/> marks the stated "none" (Kaplamasız), which is a value, not a gap.
/// </summary>
public sealed record ProductDictionaryEntryOption(string Code, string DisplayName, string Status,
    string? ParentCode, bool IsExplicitNone, decimal? MassGm2);

/// <summary>A released family with the facts it fixes for every card of it (never typed on a card).</summary>
public sealed record ProductFamilyFactsOption(Guid Id, string Code, string DisplayName, string Cins, string Tur,
    string Tip, Guid? TemplateId, string? TemplateVersion);
public sealed record ProductReferenceOptions(Guid GroupId, Guid? ClassId,
    IReadOnlyList<ProductReferenceOption> Classes, IReadOnlyList<ProductReferenceOption> Families,
    IReadOnlyList<ProductUnitOption> Units, IReadOnlyList<ProductReferenceOption> TrackingPolicies,
    IReadOnlyList<ProductTemplateOption> Templates, IReadOnlyList<string> UnavailableCatalogs,
    IReadOnlyList<ProductFamilyFactsOption>? FamilyFacts = null,
    IReadOnlyList<ProductDictionaryOption>? Dictionaries = null);

public sealed record ProductMutationResponse(ProductCardView Card, bool IsReplay);

public static class ProductApiV1
{
    public const string BasePath = "/api/v1/products";
    public const string CodeGroupsPath = "/api/v1/products/code-groups";
    public const string ReferenceOptionsPath = "/api/v1/products/reference-options";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ValidationFailed = "validation_failed";
    public const string InvalidCursor = "invalid_cursor";
    public const string NotFound = "not_found";
    public const string VersionConflict = "version_conflict";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string LifecycleConflict = "lifecycle_conflict";
}

public sealed class ProductOperationException(string code, int statusCode)
    : Exception("Ürün işlemi tamamlanamadı.")
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
