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

    /// <summary>
    /// RETIRED by K3 (Sinan 5748258299): commercial fields are accepted now, under the separate price-write
    /// permission. The name is kept for one release so a client that still maps it does not break; it is
    /// no longer thrown.
    /// </summary>
    public const string CommercialNotAvailable = "commercial_not_available";

    /// <summary>
    /// 403 (K3): a line carries a unit price, a price unit or a discount and the actor does not hold the
    /// separate price-write permission. Authority is decided before anything is read or recorded
    /// (ADR-011 §5): nothing is stored, nothing is replayed, and the draft's create permission alone
    /// never confers it. The field errors name every commercial field the request carried.
    /// </summary>
    public const string PriceWriteForbidden = "price_write_forbidden";

    /// <summary>
    /// 422 (AT-018): the named price list version no longer prices this line's product in this unit on the draft's business
    /// date (not found, another company's, ended, another version active) — field <c>lines[i].priceReference</c>. Not recorded.
    /// </summary>
    public const string PriceReferenceUnavailable = "price_reference_unavailable";

    /// <summary>422 (AT-018): the list's currency is not the draft's; nothing is converted — field <c>lines[i].priceReference</c>.</summary>
    public const string PriceReferenceCurrencyMismatch = "price_reference_currency_mismatch";

    /// <summary>503 (AT-018): the Product price-list owner is not composed in this host; never an assumed "no price".</summary>
    public const string PriceListProviderUnavailable = "price_list_provider_unavailable";

    /// <summary>422 (contract delta, card decision 3): an owner reference without an owner port is refused, never stored unvalidated.</summary>
    public const string OwnerReferenceUnavailable = "owner_reference_unavailable";

    /// <summary>
    /// 422 (SALES-A2a): a delivery address or contact reference the Cari owner does not list for the draft's party
    /// and account (unknown, or not active). A reference at a stale owner version is the terminal 409
    /// <see cref="VersionConflict"/> instead (ADR-011 §7).
    /// </summary>
    public const string OwnerReferenceInvalid = "owner_reference_invalid";

    /// <summary>503: the Product eligibility runtime is not composed, or answers an outage.</summary>
    public const string ProductEligibilityUnavailable = "product_eligibility_unavailable";

    /// <summary>503: the Cari account owner cannot answer.</summary>
    public const string CounterpartyProviderUnavailable = "counterparty_provider_unavailable";

    /// <summary>503: an owner answered a malformed value (another company, another unit, an empty identity).</summary>
    public const string ReferenceProviderInvalid = "reference_provider_invalid";

    /// <summary>
    /// 503 (AT-024): the delivery owner (Logistics) is not composed or cannot answer the order's recorded deliveries; the
    /// delivered and remaining quantities are then not shown, never assumed zero. Code-less on the wire.
    /// </summary>
    public const string DeliveryProviderUnavailable = "delivery_provider_unavailable";

    /// <summary>
    /// 503 (DEC-SAL-005): the Logistics loading owner is not composed, cannot answer, or answers a malformed value; the
    /// Sales detail then shows no loading quantity, never an assumed zero. Code-less on the wire.
    /// </summary>
    public const string LoadingProviderUnavailable = "loading_provider_unavailable";

    /// <summary>
    /// 503 (DEC-SAL-005): an impact owner that exists on main (Inventory allocations, Logistics loading) is not composed or
    /// cannot answer at the cancel command; the order is not cancelled on an unread impact. Code-less on the wire.
    /// </summary>
    public const string CancellationImpactUnavailable = "cancellation_impact_unavailable";

    /// <summary>
    /// 409 (DEC-SAL-005, contract delta): an owner records an effect of the order (an active allocation, a loading-instruction
    /// line) — the fail-closed reading refuses the cancellation; the field errors name each blocking source. Terminal for the
    /// key (recorded, replayed): after the owner's record is resolved the user reads the preview again and cancels with a new key.
    /// </summary>
    public const string CancellationBlocked = "sales_cancellation_blocked";

    /// <summary>
    /// 409 (DEC-SAL-005, contract delta): the impact now differs from the preview the command names (its token): the order is
    /// never cancelled on a preview the user did not see. Terminal for the key; the user reads the preview again.
    /// </summary>
    public const string CancellationImpactChanged = "sales_cancellation_impact_changed";

    /// <summary>
    /// 409 (DEC-SAL-002 quantity revision, contract delta): an owner records a link to the order's lines (an active allocation,
    /// a loading-instruction line) that a changed quantity would leave inconsistent — the fail-closed reading refuses the
    /// revision, exactly like the cancellation; the field errors name each blocking source. Terminal for the key (recorded,
    /// replayed): after the owner's record is resolved in its own screen the user revises again with a new key.
    /// </summary>
    public const string RevisionBlocked = "sales_revision_blocked";

    /// <summary>
    /// 503 (DEC-SAL-002 quantity revision): an owner that can link a record to the order (Inventory allocations, Logistics
    /// loading) is not composed or cannot answer; the quantity is not changed on an unread owner. Code-less on the wire.
    /// </summary>
    public const string RevisionImpactUnavailable = "revision_impact_unavailable";

    /// <summary>
    /// 409 (partial delivery remainder change, contract delta): the change would reduce a line below what an ACTIVE Inventory
    /// allocation still reserves for it (kg: the active kilograms above new quantity − shipped; a count line: any active
    /// allocation, kilograms and pieces are never compared). Sales releases nothing: the user releases or resizes the allocation in
    /// Inventory first. Terminal for the key (recorded, replayed); field errors name the blocking source.
    /// </summary>
    public const string RemainderBlocked = "sales_remainder_blocked";

    /// <summary>
    /// 503 (partial delivery remainder change): the delivery owner (Logistics' loading read) or the allocation owner is not
    /// composed or cannot answer; no remainder is changed on an unread floor. Code-less on the wire.
    /// </summary>
    public const string RemainderFloorUnavailable = "remainder_floor_unavailable";

    /// <summary>503: the Sales permission name is not registered in the central catalogue yet (setup, not a refusal).</summary>
    public const string PermissionUnregistered = "permission_unregistered";

    /// <summary>
    /// 409 (SALES-A1, contract delta): the internal-approval transition is not allowed from the draft's
    /// current approval state (e.g. approving a draft that was never submitted); a terminal refusal,
    /// recorded and replayed under the same key (ADR-011 §7). The field is <c>approvalState</c>.
    /// </summary>
    public const string StateConflict = "sales_state_conflict";

    /// <summary>422 (SALES-A1, contract delta): the rejection reason is longer than the technical bound; refused, never cut.</summary>
    public const string ApprovalReasonTooLong = "sales_approval_reason_too_long";
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
    string? ReadPermission = SalesPermissionManifestV1.DraftsRead,
    string? ApprovePermission = SalesPermissionManifestV1.OrdersApprove,
    string? PricePermission = SalesPermissionManifestV1.OrdersPriceWrite,
    string? PriceReadPermission = SalesPermissionManifestV1.OrdersPriceRead,
    string? DeliverySchedulePermission = SalesPermissionManifestV1.OrdersDeliveryScheduleWrite,
    string? CancelPermission = SalesPermissionManifestV1.OrdersCancel,
    string? QuantityPermission = SalesPermissionManifestV1.OrdersQuantityWrite,
    string? CustomerAcceptancePermission = SalesPermissionManifestV1.OrdersCustomerAcceptanceRecord,
    string? PriceFloorExceptionApprovePermission = SalesPermissionManifestV1.OrdersPriceFloorExceptionApprove);
