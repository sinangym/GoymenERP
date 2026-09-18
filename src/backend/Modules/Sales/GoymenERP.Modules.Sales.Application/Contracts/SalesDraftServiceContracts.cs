namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// The problem codes the draft routes answer beside <see cref="SalesDraftFieldCodesV1"/> and the
/// domain's <c>SalesRuleCodes</c> (#257 "HTTP result and recovery pin"; card decisions 2–4 of
/// #66). Every new code is a contract delta, marked as such in the module README.
/// </summary>
public static class SalesDraftProblemCodesV1
{
    /// <summary>400: the Idempotency-Key header is missing, repeated, blank, non-canonical, over 128 characters or malformed.</summary>
    public const string CommandMetadataInvalid = "command_metadata_invalid";

    /// <summary>400: the list cursor is malformed, another company's or another ordering's (#257).</summary>
    public const string InvalidCursor = "invalid_cursor";

    /// <summary>409: the customer account is not the party's, not this company's, or not active (#257 "company/account state").</summary>
    public const string AccountState = "account_state";

    /// <summary>409: the Product owner answers a stale product version (#262 mapping); a terminal refusal, replayed under the same key (ADR-011 §7).</summary>
    public const string VersionConflict = "version_conflict";

    /// <summary>409: the same key with another content or another actor (ADR-011 §3; #257).</summary>
    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";

    /// <summary>409: another write took the key, the draft id or the document number first; the same request may be retried.</summary>
    public const string ConcurrentChange = "concurrent_change";

    /// <summary>422: the currency has no approved money profile, or the profile is not in effect on the business date.</summary>
    public const string UnsupportedCurrency = "unsupported_currency";

    /// <summary>422 (contract delta, card decision 2): a commercial field is not accepted before T28; nothing of a price is stored.</summary>
    public const string CommercialNotAvailable = "commercial_not_available";

    /// <summary>422 (contract delta, card decision 3): an owner reference without an owner port is refused, never stored unvalidated.</summary>
    public const string OwnerReferenceUnavailable = "owner_reference_unavailable";

    /// <summary>503: the Product eligibility runtime is not composed, or answers an outage.</summary>
    public const string ProductEligibilityUnavailable = "product_eligibility_unavailable";

    /// <summary>503: the Cari account owner cannot answer.</summary>
    public const string CounterpartyProviderUnavailable = "counterparty_provider_unavailable";

    /// <summary>503: an owner answered a malformed value (another company, another unit, an empty identity).</summary>
    public const string ReferenceProviderInvalid = "reference_provider_invalid";

    /// <summary>503: the Sales permission name is not registered in the central catalogue yet (setup, not a refusal).</summary>
    public const string PermissionUnregistered = "permission_unregistered";
}

/// <summary>
/// A draft route's refusal: the HTTP status, the problem code and the field errors, exactly as the
/// route answers them (<see cref="SalesDraftProblem"/>). <see cref="IsReplay"/> marks a recorded
/// terminal refusal answered again under the same key (ADR-011 §7) with its first correlation.
/// </summary>
public sealed class SalesDraftServiceException(int status, string code,
    IReadOnlyList<SalesDraftFieldError>? errors = null, bool isReplay = false, string? originalCorrelationId = null)
    : Exception($"{status} {code}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyList<SalesDraftFieldError> Errors { get; } = errors ?? [];
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

/// <summary>
/// The permission names the draft service measures (host-bound; defaults are the Sales manifest).
/// Null means "not bound": every call is refused with a 503 setup problem, never granted.
/// </summary>
public sealed record SalesDraftServiceBindings(
    string? CreatePermission = SalesPermissionManifestV1.DraftsCreate,
    string? ReadPermission = SalesPermissionManifestV1.DraftsRead);
