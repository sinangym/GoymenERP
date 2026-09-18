namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>A row of the server-resolved company (ADR-002): its company is set once and never changes.</summary>
internal interface IFinanceTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>
/// A manual bank receipt DRAFT (FS1). It is preparation only: no money source, no bank balance,
/// no open-item or accounting effect. Every save replaces the whole preparation; <see cref="Version"/>
/// is the optimistic concurrency token and the lossless version the contract shows. The amount is
/// the exact text that passed the Settings profile's scale; no database decimal is a money scale.
/// </summary>
internal sealed class ReceiptDraftRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public string? BankReference { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// A Finance bank account (FS0, card #66 5731563923): exactly the fields of the reviewed
/// <c>BankAccountView</c>. Proposed (1) is neither active nor usable; Approved (2) is both, and
/// only a user other than the proposer approves it (Ö4, #66 5722637822). The display name and
/// currency never change here; nothing is deleted. It holds no balance and moves no money.
/// </summary>
internal sealed class BankAccountRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CurrencyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Status { get; set; }
    public long Version { get; set; }
    public string ProposedBy { get; set; } = string.Empty;
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// One accepted command attempt (idempotency): (company, actor, key) is its key, shared by every
/// Finance command, so one key is one attempt whatever the command. It keeps the canonical payload
/// hash and the result it answered, so the same key with the same payload returns that result and
/// another payload or command is refused. It names exactly one target: a draft or a bank account.
/// Append-only.
/// </summary>
internal sealed class FinanceCommandRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid? DraftId { get; set; }
    public Guid? BankAccountId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>Organization's company, referenced by identity only (the table is Organization's, not migrated here).</summary>
internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}
