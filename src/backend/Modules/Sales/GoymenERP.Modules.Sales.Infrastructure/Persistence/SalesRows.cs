namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>Every Sales table row is a tenant row of the company that owns the draft (ADR-002).</summary>
internal interface ISalesTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>Organization's company, referenced by id only; the table is Organization's (excluded from Sales's migrations).</summary>
internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// One row per company: the technical draft-number counter. Bumped with every stored draft, so
/// two drafts racing for a number cannot both commit (concurrency token). Not an official document
/// sequence (Ö8): no gap-free guarantee is given or claimed.
/// </summary>
internal sealed class DraftNumberRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public long Next { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// The stored draft header with its owner snapshots. Its lines are append-only; the header changes
/// only along the internal approval axis (SALES-A1: <see cref="ApprovalState"/> and the approval
/// facts, with the version and the update time) — nothing else moves.
/// </summary>
internal sealed class DraftRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public int Kind { get; set; }
    public int State { get; set; }
    public int Revision { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];

    /// <summary>SALES-A1: 1 Preparing, 2 PendingApproval, 3 Approved, 4 Rejected (<c>InternalApprovalState</c>); the facts follow the state (check constraint).</summary>
    public int ApprovalState { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }

    public Guid? DeliveryAddressId { get; set; }
    public string? DeliveryAddressVersion { get; set; }
    public Guid? ContactId { get; set; }
    public string? ContactVersion { get; set; }
    public Guid? ResponsibleId { get; set; }
    public string? ResponsibleVersion { get; set; }
    public Guid? PaymentTermsId { get; set; }
    public string? PaymentTermsVersion { get; set; }
    public Guid? SourceStrategyId { get; set; }
    public string? SourceStrategyVersion { get; set; }
    public Guid? PackageId { get; set; }
    public string? PackageVersion { get; set; }

    public string MoneyOperation { get; set; } = string.Empty;
    public Guid MoneyProfileId { get; set; }
    public string MoneyProfileVersion { get; set; } = string.Empty;
    public DateOnly MoneyEffectiveOn { get; set; }
    public bool MoneyIsActive { get; set; }
    public DateOnly MoneyEffectiveFrom { get; set; }
    public DateOnly? MoneyEffectiveThrough { get; set; }
    public int MoneyScale { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One stored line with the Product and Settings owners' snapshots as answered. Append-only.</summary>
internal sealed class DraftLineRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }

    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateOnly ProductEffectiveOn { get; set; }

    public Guid StockUnitCompanyId { get; set; }
    public Guid StockUnitId { get; set; }
    public Guid StockUnitProfileId { get; set; }
    public string StockUnitProfileVersion { get; set; } = string.Empty;
    public Guid SalesUnitCompanyId { get; set; }
    public Guid SalesUnitId { get; set; }
    public Guid SalesUnitProfileId { get; set; }
    public string SalesUnitProfileVersion { get; set; } = string.Empty;
    public Guid OrderUnitBindingCompanyId { get; set; }
    public Guid OrderUnitBindingUnitId { get; set; }
    public Guid OrderUnitBindingProfileId { get; set; }
    public string OrderUnitBindingProfileVersion { get; set; } = string.Empty;

    public Guid OrderUnitCompanyId { get; set; }
    public Guid OrderUnitId { get; set; }
    public string OrderUnitCode { get; set; } = string.Empty;
    public string OrderUnitDimensionCode { get; set; } = string.Empty;
    public Guid OrderUnitProfileId { get; set; }
    public string OrderUnitProfileVersion { get; set; } = string.Empty;
    public bool OrderUnitIsKilogram { get; set; }
    public int OrderUnitProfileScale { get; set; }
    public int OrderUnitScale { get; set; }
    public bool OrderUnitIsActive { get; set; }
    public DateOnly OrderUnitEffectiveFrom { get; set; }
    public DateOnly? OrderUnitEffectiveThrough { get; set; }

    public decimal Quantity { get; set; }
    public string QuantityText { get; set; } = string.Empty;
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }
    public string? UnitPrice { get; set; }
    public Guid? PriceUnitId { get; set; }
    public string? DiscountPercent { get; set; }
}

/// <summary>One requested delivery slice of a line. Append-only.</summary>
internal sealed class DraftDeliverySliceRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public int LineNo { get; set; }
    public int SliceNo { get; set; }
    public Guid SliceId { get; set; }
    public Guid OrderUnitId { get; set; }
    public decimal Quantity { get; set; }
    public string QuantityText { get; set; } = string.Empty;
    public DateOnly RequestedDeliveryDate { get; set; }
}

/// <summary>
/// The ADR-011 attempt record: (company, command name, uppercase SHA-256 of the trimmed idempotency
/// key) is the attempt identity — the raw key is never stored; the canonical request hash is its
/// content, and the actor is stored to be compared, never only kept. Written with the draft in the
/// same save; a replay reads it first. Append-only.
/// </summary>
internal sealed class DraftAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored draft (<see cref="DraftId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set) — ADR-011 §7.</summary>
    public int Outcome { get; set; }
    public Guid? DraftId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// K3 Head B (Sinan 5748258299): an APPROVED revision archived the moment its price or discount was
/// changed — the approval facts, the money profile it was approved under, who archived it and when.
/// Written in the same save as the draft's advance to the next revision. Append-only, never changed.
/// </summary>
internal sealed class DraftRevisionRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public int Revision { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; set; }
    public Guid MoneyProfileId { get; set; }
    public string MoneyProfileVersion { get; set; } = string.Empty;
    public int MoneyScale { get; set; }
    public DateTimeOffset ArchivedAtUtc { get; set; }
    public string ArchivedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>A line of an archived approved revision: identity, quantity text and the commercial fields exactly as they were. Append-only.</summary>
internal sealed class DraftRevisionLineRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public int Revision { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public Guid OrderUnitId { get; set; }
    public string QuantityText { get; set; } = string.Empty;
    public string? UnitPrice { get; set; }
    public Guid? PriceUnitId { get; set; }
    public string? DiscountPercent { get; set; }
}
