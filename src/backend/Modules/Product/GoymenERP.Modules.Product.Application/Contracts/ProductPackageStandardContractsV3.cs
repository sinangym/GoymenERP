namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// SAT-D382 / SAT-D383 / SAT-D397 / SAT-D401 (STK-SC36-c): the package standard table gains a customer-specific override, an
/// approval with an effective date and a kept import file with its result report.
/// <list type="bullet">
/// <item><b>Default and override</b>: the product's entry is the default; a customer's entry for the same product overrides it
/// (no plant override). Both live in the same insert-only table version.</item>
/// <item><b>Approval</b>: every new version (import, product edit, customer edit) is proposed as Pending and takes effect only
/// once approved, from the approver's effective date (never earlier than today's business day). At most one version waits at a
/// time; a rejected version never takes effect. Versions written before this slice are effective without a decision.</item>
/// <item><b>Files</b>: every import file that reaches the row check is kept with its result report (accepted or refused), as long
/// as the audit record (SAT-D383). No retention period is chosen and no deletion or destruction exists (SAT-D397 / SAT-D401;
/// the retention decision stays open). The original file is not served back: a download needs the document vault and its scan.</item>
/// <item><b>Read port</b> (<see cref="IProductPackageStandardReaderV3"/>): the customer's override if any, otherwise the product
/// default, of the approved version effective on the consumer's business date. Old documents keep their snapshot (URT-D27).</item>
/// </list>
/// </summary>
public static class ProductPackageStandardPermissionsV3
{
    /// <summary>
    /// The approver's name (approve or reject the pending version). Registered, not granted: who approves is a human grant gate,
    /// and whether the preparer may approve is <see cref="ProductPackageStandardApprovalOptionsV3"/> (closed by default).
    /// </summary>
    public const string Approve = "product.package-standards.approve";
}

public static class ProductPackageStandardProblemCodesV3
{
    /// <summary>Another version waits for approval; it must be decided before a new one is proposed.</summary>
    public const string ApprovalPending = "product_package_standard_approval_pending";

    /// <summary>The named version is not the one waiting for approval (decided already, or another one).</summary>
    public const string VersionNotPending = "product_package_standard_version_not_pending";

    /// <summary>The person who prepared the version may not decide it (closed by default).</summary>
    public const string PreparerCannotDecide = "product_package_standard_preparer_cannot_decide";

    /// <summary>The effective date is missing, malformed or earlier than today's business day.</summary>
    public const string EffectiveDateInvalid = "product_package_standard_effective_date_invalid";

    /// <summary>A passive (inactive) product cannot get a package standard entry (SAT-D383).</summary>
    public const string ProductInactive = "product_package_standard_product_inactive";

    /// <summary>The customer is not an active account of an active party in this company.</summary>
    public const string CustomerNotFound = "product_package_standard_customer_not_found";

    /// <summary>The Cari owner cannot answer: fail-closed, the override is not stored.</summary>
    public const string CustomerProviderUnavailable = "product_package_standard_customer_provider_unavailable";
}

/// <summary>A version's approval state as the views name it.</summary>
public static class ProductPackageStandardVersionStatesV3
{
    /// <summary>Written before approvals existed: effective without a decision.</summary>
    public const string Legacy = "Legacy";

    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class ProductPackageStandardDecisionsV3
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class ProductPackageStandardImportOutcomesV3
{
    public const string Accepted = "Accepted";
    public const string Refused = "Refused";
}

/// <summary>Which entry answered a read.</summary>
public static class ProductPackageStandardEntryScopesV3
{
    public const string Product = "Product";
    public const string Customer = "Customer";
}

public static class ProductPackageStandardListLimitsV3
{
    /// <summary>The newest kept import files listed at once.</summary>
    public const int MaximumListedImports = 50;

    /// <summary>The newest versions listed at once.</summary>
    public const int MaximumListedVersions = 50;
}

/// <summary>
/// Whether the person who prepared a version may also decide it. Not decided (NEEDS-SINAN); the default is the closed answer
/// (the preparer may not decide). Hosts may only bind it from reviewed configuration.
/// </summary>
public sealed record ProductPackageStandardApprovalOptionsV3(bool PreparerMayDecide)
{
    public static ProductPackageStandardApprovalOptionsV3 Closed { get; } = new(false);
}

/// <summary>Approve the pending version from <c>EffectiveFrom</c> (<c>yyyy-MM-dd</c>, today or later) with a reason.</summary>
public sealed record ApproveProductPackageStandardVersionRequestV3(string? EffectiveFrom, string? Reason);

/// <summary>Reject the pending version with a reason; it never takes effect.</summary>
public sealed record RejectProductPackageStandardVersionRequestV3(string? Reason);

/// <summary>A customer's override entry. The customer code and name come from the Cari owner (null when it cannot answer).</summary>
public sealed record ProductPackageStandardCustomerEntryV3(
    Guid CustomerId,
    string? CustomerCode,
    string? CustomerName,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    int PiecesPerPackage);

/// <summary>One change the pending version makes against the version it was prepared from (null = no entry).</summary>
public sealed record ProductPackageStandardChangeV3(
    Guid? CustomerId,
    string? CustomerName,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    int? PiecesBefore,
    int? PiecesAfter);

/// <summary>A version's facts and its decision (if any).</summary>
public sealed record ProductPackageStandardVersionSummaryV3(
    string Version,
    string State,
    string SourceKind,
    string? SourceLabel,
    string? FileName,
    Guid? EditedProductId,
    Guid? EditedCustomerId,
    string Reason,
    int EntryCount,
    int CustomerEntryCount,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string? EffectiveFrom,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);

/// <summary>
/// The approval picture: the head version (the expected version of the next command), today's business day, the version in
/// effect today with its customer overrides, the pending version with its changes, and the newest versions.
/// </summary>
public sealed record ProductPackageStandardApprovalViewV3(
    Guid CompanyId,
    string HeadVersion,
    string Today,
    string? EffectiveVersion,
    string? EffectiveFrom,
    bool CustomerNamesAvailable,
    IReadOnlyList<ProductPackageStandardCustomerEntryV3> CustomerEntries,
    ProductPackageStandardVersionSummaryV3? Pending,
    IReadOnlyList<ProductPackageStandardChangeV3> PendingChanges,
    IReadOnlyList<ProductPackageStandardVersionSummaryV3> RecentVersions);

/// <summary>A kept import file (the file itself is not served back).</summary>
public sealed record ProductPackageStandardImportFileV3(
    Guid ImportId,
    string FileName,
    string FileSha256,
    int FileSizeBytes,
    string Outcome,
    string? ProblemCode,
    string? Version,
    string SourceLabel,
    int RowCount,
    int ErrorCount,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

public sealed record ProductPackageStandardImportFileListV3(Guid CompanyId, IReadOnlyList<ProductPackageStandardImportFileV3> Items);

/// <summary>The result report kept with an import file: every row error, not only the first ones the answer lists.</summary>
public sealed record ProductPackageStandardImportReportV3(
    Guid ImportId,
    string FileName,
    string FileSha256,
    string Outcome,
    string? ProblemCode,
    string Detail,
    int RowCount,
    int AcceptedCount,
    IReadOnlyList<string> Errors,
    string? Version,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

/// <summary>
/// The approval, override and file surface. Reads and edits measure <see cref="ProductPackageStandardPermissionsV2.Manage"/>; the
/// approver's read and decisions measure <see cref="ProductPackageStandardPermissionsV3.Approve"/>; both company-wide.
/// </summary>
public interface IProductPackageStandardServiceV3
{
    Task<ProductPackageStandardApprovalViewV3> GetApprovalStateAsync(CancellationToken cancellationToken = default);

    Task<ProductPackageStandardApprovalViewV3> GetApprovalStateForApproverAsync(CancellationToken cancellationToken = default);

    Task<ProductPackageStandardImportFileListV3> ListImportsAsync(CancellationToken cancellationToken = default);

    /// <summary>The kept report, or null when no such import file exists in the company.</summary>
    Task<ProductPackageStandardImportReportV3?> GetImportReportAsync(Guid importId, CancellationToken cancellationToken = default);

    Task<ProductPackageStandardResultV2> SetCustomerAsync(Guid customerId, Guid productId, SetProductPackageStandardRequestV2 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default);

    Task<ProductPackageStandardResultV2> ApproveAsync(long version, ApproveProductPackageStandardVersionRequestV3 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default);

    Task<ProductPackageStandardResultV2> RejectAsync(long version, RejectProductPackageStandardVersionRequestV3 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken cancellationToken = default);
}

/// <summary>The Cari owner's answer for customer identities (active account of an active party in this company).</summary>
public enum ProductPackageStandardCustomerOutcomeV3
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

public sealed record ProductPackageStandardCustomerV3(Guid CustomerId, string Code, string Name);

/// <summary><see cref="Customers"/> holds only the asked identities that are active customers of this company.</summary>
public sealed record ProductPackageStandardCustomerAnswerV3(
    ProductPackageStandardCustomerOutcomeV3 Outcome,
    IReadOnlyList<ProductPackageStandardCustomerV3> Customers);

/// <summary>
/// The Product-owned port to the Cari owner for an override's customer. The host implements it over the authorized Counterparty
/// display read; the default answers Unavailable (fail-closed: no override is stored, names are not shown).
/// </summary>
public interface IProductPackageStandardCustomerPortV3
{
    Task<ProductPackageStandardCustomerAnswerV3> ReadAsync(IReadOnlyList<Guid> customerIds,
        CancellationToken cancellationToken = default);
}

/// <summary>A read of the value in effect. Without a value, every other field is null.</summary>
public sealed record ProductPackageStandardReadV3(
    ProductPackageStandardReadOutcomeV2 Outcome,
    int? PiecesPerPackage,
    long? StandardVersion,
    Guid? StandardVersionId,
    string? SourceKind,
    string? SourceLabel,
    string? EntryScope,
    DateOnly? EffectiveFrom)
{
    public static ProductPackageStandardReadV3 Without(ProductPackageStandardReadOutcomeV2 outcome) =>
        new(outcome, null, null, null, null, null, null, null);
}

/// <summary>
/// SAT-D382: the consumers' (Sales, Production) read port. <paramref name="customerId"/> null reads the product default only;
/// <paramref name="on"/> is the consumer's business date. Access only (authenticated member of the company), no permission;
/// NoSource while no effective entry exists; Unavailable on any storage failure (never throws).
/// </summary>
public interface IProductPackageStandardReaderV3
{
    Task<ProductPackageStandardReadV3> GetAsync(Guid productId, Guid? customerId, DateOnly on,
        CancellationToken cancellationToken = default);
}
