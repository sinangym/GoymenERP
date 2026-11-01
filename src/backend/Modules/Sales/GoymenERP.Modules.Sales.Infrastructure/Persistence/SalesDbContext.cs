using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// Sales's own tables in the <c>sales</c> schema (T03 / SALES-P2, ADR-002, ADR-009).
/// <list type="bullet">
/// <item>Every row is a tenant row of the company that owns the draft: a global query filter on the
/// server-resolved company, and a write guard that refuses any row of another company, any company
/// change and any write without a resolved company.</item>
/// <item>ADR-011 / R3 identities: the attempt key (company, command name, uppercase SHA-256 of the
/// trimmed idempotency key) is a primary key under a binary collation — the raw key is never stored,
/// the same key of another company or another command is another attempt; the document number is
/// unique per company; a client line / slice id is unique per draft.</item>
/// <item>A draft, its lines, its slices and its attempt record are append-only in this slice; only
/// the draft's version may move (approval / revision are T33). The number counter changes its
/// counter only. Nothing is deleted.</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "sales";

    /// <summary>Binary collation: equality is exact, as the domain's ordinal identity comparison.</summary>
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    /// <summary>
    /// SALES-A1 (CK_Drafts_Approval): Preparing (1) carries no fact; PendingApproval (2) the submission
    /// only; Approved (3) the submission and the decision, no reason; Rejected (4) the submission and the
    /// decision, the reason optional. D2 (Sinan 5749046401): Withdrawn (5) the decision — the withdrawer and
    /// the time — with the submission either whole or absent (withdrawn from Preparing), the reason optional.
    /// The same rule the domain restores by (<c>RestoreApproval</c>).
    /// </summary>
    internal const string ApprovalConstraint =
        "([ApprovalState] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0)) "
        + "OR ([ApprovalState] = 5 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND (([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL)) AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0))";

    internal const string CustomerAcceptanceConstraint =
        "([CustomerAcceptanceRequired] = 0 AND [CustomerAcceptedRevision] IS NULL AND [CustomerAcceptedBy] IS NULL AND [CustomerAcceptedAtUtc] IS NULL) "
        + "OR ([CustomerAcceptanceRequired] = 1 AND (([CustomerAcceptedRevision] IS NULL AND [CustomerAcceptedBy] IS NULL AND [CustomerAcceptedAtUtc] IS NULL) "
        + "OR ([CustomerAcceptedRevision] IS NOT NULL AND [CustomerAcceptedRevision] >= 0 AND [CustomerAcceptedRevision] <= [Revision] "
        + "AND ([CustomerAcceptedRevision] < [Revision] OR [ApprovalState] = 3) "
        + "AND [CustomerAcceptedBy] IS NOT NULL AND LEN([CustomerAcceptedBy]) > 0 AND [CustomerAcceptedAtUtc] IS NOT NULL)))";

    /// <summary>
    /// UX_DraftAttempts_DraftId: a stored draft is born from exactly one create attempt. Since SALES-A1 the
    /// state commands (submit / approve / reject) record their own attempts against the same draft, one per
    /// key and as many as the axis allows (a rejected draft is submitted again), so the uniqueness is
    /// filtered to the create command; the attempt key (CompanyId, CommandName, IdempotencyKeyHash) stays the
    /// primary key for every command.
    /// </summary>
    internal const string CreateAttemptFilter = "[DraftId] IS NOT NULL AND [CommandName] = N'" + SalesDraftCommands.CreateV1 + "'";

    /// <summary>
    /// A successful attempt always identifies its draft and has no refusal. New create-time terminal
    /// refusals have no draft and new mutation refusals identify the existing draft; null remains valid
    /// for legacy mutation refusals written before that identity was retained, so upgrades preserve replay.
    /// </summary>
    internal const string AttemptOutcomeConstraint =
        "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND ([DraftId] IS NULL OR [DraftId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";

    /// <summary>
    /// ADR-SAL-001 §2 (API-22 / API-24): the quote is its own aggregate, so its create / revise / lose commands record their
    /// attempts in their OWN table, the draft attempt's shape (CK_QuoteAttempts_Outcome). The conversion (No 162, ADR-SAL-001
    /// §3) is NOT a quote attempt: it creates the ORDER, so its one attempt is the order's, in <c>sales.DraftAttempts</c>.
    /// </summary>
    internal const string QuoteAttemptOutcomeConstraint =
        "([Outcome] = 1 AND [QuoteId] IS NOT NULL AND [QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND ([QuoteId] IS NULL OR [QuoteId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";

    /// <summary>
    /// CK_QuoteAttempts_Command: only the quote's own versioned commands record here (never the conversion). The retired
    /// <c>sales.quote.lose.v1</c> stays admitted: its recorded attempts are history and are never rewritten (SAT-D25 added v2).
    /// </summary>
    internal const string QuoteAttemptCommandConstraint =
        "[CommandName] IN (N'" + SalesQuoteCommands.CreateV1 + "', N'" + SalesQuoteCommands.ReviseV1 + "', N'"
        + SalesQuoteCommands.LoseV1 + "', N'" + SalesQuoteCommands.LoseV2 + "', N'" + SalesQuoteCommands.ApproveExpiredConversionV1 + "')";

    /// <summary>CK_QuoteAttempts_Result (ADR-SAL-001 §2/§4): no quote command stores a response snapshot.</summary>
    internal const string QuoteAttemptResultConstraint = "[ResultJson] IS NULL";

    /// <summary>UX_QuoteAttempts_QuoteId: a stored quote is born from exactly one create attempt.</summary>
    internal const string QuoteCreateAttemptFilter = "[QuoteId] IS NOT NULL AND [CommandName] = N'" + SalesQuoteCommands.CreateV1 + "'";

    /// <summary>
    /// ADR-SAL-001 §6: the SQL trigger that refuses any UPDATE or DELETE of a quote attempt (THROW 51068, used by no other
    /// migration), beside the EF write guard.
    /// </summary>
    internal const string QuoteAttemptImmutabilityTrigger = "TR_QuoteAttempts_RejectMutation";

    /// <summary>UX_Drafts_SourceQuoteId (No 162): one order per quote — the database itself refuses a second.</summary>
    internal const string SourceQuoteFilter = "[SourceQuoteId] IS NOT NULL";

    /// <summary>UX_QuoteRequests_ActiveRequest (No 163, talep → teklif N:1): a request is covered by at most one open or converted quote.</summary>
    internal const string ActiveQuoteRequestFilter = "[IsActive] = 1";

    /// <summary>
    /// CK_Quotes_Lifecycle: Open (1) carries neither a loss nor a conversion; Lost (3) the loss only — either a SAT-D25 list
    /// entry with its complete snapshot (the explanation optional, never blank) or, recorded before SAT-D25, a non-blank
    /// free-text reason without an entry; Converted (2) the conversion only. The rule the domain restores by.
    /// </summary>
    internal const string QuoteLifecycleConstraint =
        "([State] = 1 AND [LossReason] IS NULL AND " + NoLossReasonEntry + " AND [LostBy] IS NULL AND [LostAtUtc] IS NULL AND [ConvertedDraftId] IS NULL AND [ConvertedBy] IS NULL AND [ConvertedAtUtc] IS NULL) "
        + "OR ([State] = 3 AND [LostBy] IS NOT NULL AND LEN([LostBy]) > 0 AND [LostAtUtc] IS NOT NULL "
        + "AND (([LossReasonId] IS NULL AND [LossReason] IS NOT NULL AND LEN([LossReason]) > 0 AND " + NoLossReasonEntry + ") "
        + "OR ([LossReasonId] IS NOT NULL AND [LossReasonId] <> '00000000-0000-0000-0000-000000000000' AND [LossReasonCode] IS NOT NULL AND LEN([LossReasonCode]) > 0 "
        + "AND [LossReasonLabel] IS NOT NULL AND LEN([LossReasonLabel]) > 0 AND [LossReasonProfileId] IS NOT NULL "
        + "AND [LossReasonProfileId] <> '00000000-0000-0000-0000-000000000000' AND [LossReasonProfileVersion] IS NOT NULL AND [LossReasonProfileVersion] > 0 "
        + "AND ([LossReason] IS NULL OR LEN([LossReason]) > 0))) "
        + "AND [ConvertedDraftId] IS NULL AND [ConvertedBy] IS NULL AND [ConvertedAtUtc] IS NULL) "
        + "OR ([State] = 2 AND [ConvertedDraftId] IS NOT NULL AND [ConvertedDraftId] <> '00000000-0000-0000-0000-000000000000' AND [ConvertedBy] IS NOT NULL "
        + "AND LEN([ConvertedBy]) > 0 AND [ConvertedAtUtc] IS NOT NULL AND [LossReason] IS NULL AND " + NoLossReasonEntry + " AND [LostBy] IS NULL AND [LostAtUtc] IS NULL)";

    private const string NoLossReasonEntry =
        "[LossReasonId] IS NULL AND [LossReasonCode] IS NULL AND [LossReasonLabel] IS NULL AND [LossReasonProfileId] IS NULL AND [LossReasonProfileVersion] IS NULL";


    /// <summary>
    /// ADR-SAL-001 §2 (API-20 / API-21): the sales request is its own aggregate, so its commands record their attempts in their
    /// OWN table, the draft attempt's shape: a stored outcome names its request and carries no refusal; a recorded terminal
    /// refusal carries its JSON and, when the request is known, its request (CK_RequestAttempts_Outcome).
    /// </summary>
    internal const string RequestAttemptOutcomeConstraint =
        "([Outcome] = 1 AND [RequestId] IS NOT NULL AND [RequestId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND ([RequestId] IS NULL OR [RequestId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";

    /// <summary>CK_RequestAttempts_Command: only the sales request's own versioned commands record here.</summary>
    internal const string RequestAttemptCommandConstraint =
        "[CommandName] IN (N'" + SalesRequestCommands.CreateV1 + "', N'" + SalesRequestCommands.ApproveV1 + "', N'"
        + SalesRequestCommands.RejectV1 + "')";

    /// <summary>
    /// CK_RequestAttempts_Result (ADR-SAL-001 §2/§4): the column keeps the attempt shape, but no request command stores a
    /// response snapshot — every request answer is reproduced from the stored request.
    /// </summary>
    internal const string RequestAttemptResultConstraint = "[ResultJson] IS NULL";

    /// <summary>UX_RequestAttempts_RequestId: a stored request is born from exactly one create attempt (the draft's rule).</summary>
    internal const string RequestCreateAttemptFilter = "[RequestId] IS NOT NULL AND [CommandName] = N'" + SalesRequestCommands.CreateV1 + "'";

    /// <summary>
    /// ADR-SAL-001 §6: the SQL trigger that refuses any UPDATE or DELETE of a request attempt (THROW 51067, used by no other
    /// migration), beside the EF write guard. Declared on the model so EF writes the table without an OUTPUT clause.
    /// </summary>
    internal const string RequestAttemptImmutabilityTrigger = "TR_RequestAttempts_RejectMutation";

    /// <summary>
    /// CK_Requests_Decision (API-21): Submitted (1) carries no decision; Approved (2) a decision without a reason; Rejected (3)
    /// a decision with a non-blank reason (No 161: "Ret … gerekçe ile"). The rule the domain restores by.
    /// </summary>
    internal const string RequestDecisionConstraint =
        "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([State] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL) "
        + "OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)";
    /// <summary>
    /// CK_DraftAttempts_Result: a recorded success of the price-floor (PL04) or discount (SAT-D2 / API-12) exception approval
    /// keeps its first complete answer as JSON; every other attempt keeps none.
    /// </summary>
    internal const string AttemptResultConstraint =
        "([CommandName] IN (N'sales.order-draft.approve-price-floor-exceptions.v1', N'sales.order-draft.approve-discount-exceptions.v1') AND [Outcome] = 1 "
        + "AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1) OR "
        + "(([CommandName] NOT IN (N'sales.order-draft.approve-price-floor-exceptions.v1', N'sales.order-draft.approve-discount-exceptions.v1') OR [Outcome] <> 1) AND [ResultJson] IS NULL)";

    /// <summary>The CK_DraftAttempts_Result before API-12 (price-floor approval only); the discount-exception migration's Down restores it.</summary>
    internal const string AttemptResultConstraintBeforeDiscountExceptions =
        "([CommandName] = N'sales.order-draft.approve-price-floor-exceptions.v1' AND [Outcome] = 1 "
        + "AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1) OR "
        + "(([CommandName] <> N'sales.order-draft.approve-price-floor-exceptions.v1' OR [Outcome] <> 1) AND [ResultJson] IS NULL)";

    internal DbSet<DraftNumberRow> DraftNumbers => Set<DraftNumberRow>();

    internal DbSet<DraftRow> Drafts => Set<DraftRow>();

    internal DbSet<DraftLineRow> DraftLines => Set<DraftLineRow>();

    internal DbSet<DraftDeliverySliceRow> DraftDeliverySlices => Set<DraftDeliverySliceRow>();

    internal DbSet<DraftAttemptRow> DraftAttempts => Set<DraftAttemptRow>();

    internal DbSet<DraftRevisionRow> DraftRevisions => Set<DraftRevisionRow>();

    internal DbSet<DraftRevisionLineRow> DraftRevisionLines => Set<DraftRevisionLineRow>();

    /// <summary>API-04: the header of each archived approved revision (append-only, one per archive).</summary>
    internal DbSet<DraftRevisionHeaderRow> DraftRevisionHeaders => Set<DraftRevisionHeaderRow>();

    /// <summary>DEC-SAL-003: the recorded delivery-schedule changes (append-only).</summary>
    internal DbSet<DraftDeliveryScheduleRow> DraftDeliverySchedules => Set<DraftDeliveryScheduleRow>();

    /// <summary>DEC-SAL-003: the complete dates of each recorded schedule (append-only).</summary>
    internal DbSet<DraftDeliveryScheduleDateRow> DraftDeliveryScheduleDates => Set<DraftDeliveryScheduleDateRow>();

    /// <summary>DEC-SAL-005 / AT-007: the recorded cancellation of an approved order (append-only, at most one per draft).</summary>
    internal DbSet<DraftCancellationRow> DraftCancellations => Set<DraftCancellationRow>();

    internal DbSet<DraftCustomerRejectionRow> DraftCustomerRejections => Set<DraftCustomerRejectionRow>();

    /// <summary>No 30 / SAT-D12: the recorded force-closures (append-only, at most one per draft).</summary>
    internal DbSet<DraftForceClosureRow> DraftForceClosures => Set<DraftForceClosureRow>();

    /// <summary>Partial delivery (user decision 2026-09-30): the requested remainder changes (append-only).</summary>
    internal DbSet<DraftRemainderChangeRow> DraftRemainderChanges => Set<DraftRemainderChangeRow>();

    /// <summary>Partial delivery: the decision on each remainder change (append-only, at most one per change).</summary>
    internal DbSet<DraftRemainderDecisionRow> DraftRemainderDecisions => Set<DraftRemainderDecisionRow>();

    /// <summary>API-20: the display-number counters of the non-draft Sales records, one row per company and series.</summary>
    internal DbSet<DocumentCounterRow> DocumentCounters => Set<DocumentCounterRow>();

    /// <summary>API-20 / API-21: the sales requests (header; the decision is the only change).</summary>
    internal DbSet<RequestRow> Requests => Set<RequestRow>();

    /// <summary>API-20: the requests' lines with the owners' snapshots (append-only).</summary>
    internal DbSet<RequestLineRow> RequestLines => Set<RequestLineRow>();

    /// <summary>API-22…24: the sales quotes (header; the lifecycle is the only change).</summary>
    internal DbSet<QuoteRow> Quotes => Set<QuoteRow>();

    /// <summary>API-22: every recorded quote revision's terms (append-only).</summary>
    internal DbSet<QuoteRevisionRow> QuoteRevisions => Set<QuoteRevisionRow>();

    /// <summary>API-22: every quote revision's lines (append-only).</summary>
    internal DbSet<QuoteLineRow> QuoteLines => Set<QuoteLineRow>();

    /// <summary>No 163: the requests a quote covers (active while the quote is open or converted).</summary>
    internal DbSet<QuoteRequestRow> QuoteRequests => Set<QuoteRequestRow>();

    /// <summary>API-22 / API-24 (ADR-SAL-001 §2): the quote commands' own attempt records (append-only).</summary>
    internal DbSet<QuoteAttemptRow> QuoteAttempts => Set<QuoteAttemptRow>();

    /// <summary>SAT-D24 / SAT-D44: the expired-quote approvals, one per quote revision, append-only.</summary>
    internal DbSet<QuoteExpiryApprovalRow> QuoteExpiryApprovals => Set<QuoteExpiryApprovalRow>();

    /// <summary>API-20 / API-21 (ADR-SAL-001 §2): the request commands' own attempt records (append-only).</summary>
    internal DbSet<RequestAttemptRow> RequestAttempts => Set<RequestAttemptRow>();

    /// <summary>CK_DraftRemainderChanges_Kind: a cancel carries no quantity; a revise carries a positive quantity and its text.</summary>
    internal const string RemainderKindConstraint =
        "([Kind] = 1 AND [RequestedQuantity] IS NULL AND [RequestedQuantityText] IS NULL) "
        + "OR ([Kind] = 2 AND [RequestedQuantity] > 0 AND [RequestedQuantityText] IS NOT NULL AND LEN([RequestedQuantityText]) > 0)";

    /// <summary>CK_DraftRemainderChanges_Floor: the owner's totals in their own order, a recorded shipment (a partly delivered line).</summary>
    internal const string RemainderFloorConstraint =
        "[DeliveredQuantity] >= 0 AND [DeliveredQuantity] <= [ShippedQuantity] AND [ShippedQuantity] <= [LoadedQuantity] AND [ShippedQuantity] > 0";

    /// <summary>
    /// CK_DraftRemainderDecisions_Outcome: an approval (2) carries the resulting quantity and the owner's totals read at the
    /// approval, no reason; a rejection (3) carries none of those and an optional non-blank reason.
    /// </summary>
    internal const string RemainderOutcomeConstraint =
        "([Outcome] = 2 AND [ResultQuantity] > 0 AND [ResultQuantityText] IS NOT NULL AND LEN([ResultQuantityText]) > 0 AND [Reason] IS NULL "
        + "AND [LoadedQuantity] IS NOT NULL AND [ShippedQuantity] IS NOT NULL AND [DeliveredQuantity] IS NOT NULL "
        + "AND [DeliveredQuantity] >= 0 AND [DeliveredQuantity] <= [ShippedQuantity] AND [ShippedQuantity] <= [LoadedQuantity] AND [ResultQuantity] >= [LoadedQuantity]) "
        + "OR ([Outcome] = 3 AND [ResultQuantity] IS NULL AND [ResultQuantityText] IS NULL AND [LoadedQuantity] IS NULL AND [ShippedQuantity] IS NULL "
        + "AND [DeliveredQuantity] IS NULL AND ([Reason] IS NULL OR LEN([Reason]) > 0))";

    /// <summary>
    /// CK_Drafts_State: the kind is Order; the lifecycle is Draft (1) or, since DEC-SAL-005, Cancelled (2) or, since No 30 /
    /// SAT-D12, Closed (3) — and only an APPROVED order (approval state 3) is ever cancelled or closed; the revision is a counter.
    /// </summary>
    internal const string StateConstraint = "[Kind] = 1 AND [State] IN (1, 2, 3) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)";

    /// <summary>The CK_Drafts_State before No 30 (Draft or Cancelled); the force-closure migration's Down restores it.</summary>
    internal const string StateConstraintBeforeForceClose =
        "[Kind] = 1 AND [State] IN (1, 2) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)";

    /// <summary>
    /// CK_DraftForceClosures_Identity: identities, the revision counter, the smallest version a closure can produce (create 1,
    /// one line 2, submit 3, approve 4, close 5) and non-blank texts.
    /// </summary>
    internal const string ForceClosureIdentityConstraint =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [Revision] >= 0 AND [DraftVersion] >= 5 AND LEN([ClosedBy]) > 0 AND LEN([Reason]) > 0 "
        + "AND LEN([ImpactToken]) > 0 AND LEN([CorrelationId]) > 0";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureDraftNumbers(modelBuilder);
        ConfigureDrafts(modelBuilder);
        ConfigureDraftLines(modelBuilder);
        ConfigureDraftDeliverySlices(modelBuilder);
        ConfigureDraftAttempts(modelBuilder);
        ConfigureDraftRevisions(modelBuilder);
        ConfigureDraftDeliverySchedules(modelBuilder);
        ConfigureDraftCancellations(modelBuilder);
        ConfigureDraftCustomerRejections(modelBuilder);
        ConfigureDraftForceClosures(modelBuilder);
        ConfigureDraftRemainderChanges(modelBuilder);
        ConfigureRequests(modelBuilder);
        ConfigureQuotes(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureDraftNumbers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftNumberRow>();
        entity.ToTable("DraftNumbers", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftNumbers_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftNumbers_Next", "[Next] >= 1");
            table.HasCheckConstraint("CK_DraftNumbers_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.CompanyId);
        entity.Property(x => x.CompanyId).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftRow>();
        entity.ToTable("Drafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_Drafts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000'");
            // K3 Head B: the header's Revision is a counter (0 at creation, +1 per archived approved revision), no longer
            // R3's "= 0" pin; Kind stays pinned. AddDraftRevisions widens the constraint. DEC-SAL-005: the lifecycle State is
            // Draft or Cancelled, and Cancelled only beside an approved order (AddDraftCancellations widens it again).
            table.HasCheckConstraint("CK_Drafts_State", StateConstraint);
            // SALES-A1: the approval facts follow the approval state exactly (a row cannot say "approved" without a decision).
            table.HasCheckConstraint("CK_Drafts_Approval", ApprovalConstraint);
            table.HasCheckConstraint("CK_Drafts_CustomerAcceptance", CustomerAcceptanceConstraint);
            // No 162: a source quote always names the revision whose snapshot the order carries.
            table.HasCheckConstraint("CK_Drafts_SourceQuote",
                "([SourceQuoteId] IS NULL AND [SourceQuoteRevision] IS NULL) OR ([SourceQuoteId] IS NOT NULL AND [SourceQuoteId] <> '00000000-0000-0000-0000-000000000000' AND [SourceQuoteRevision] IS NOT NULL AND [SourceQuoteRevision] >= 0)");
            table.HasCheckConstraint("CK_Drafts_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_Drafts_Text",
                "LEN([DocumentNo]) > 0 AND LEN([MoneyOperation]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Drafts_Money", "[MoneyScale] >= 0 AND ([MoneyEffectiveThrough] IS NULL OR [MoneyEffectiveThrough] >= [MoneyEffectiveFrom])");
            table.HasCheckConstraint("CK_Drafts_References",
                "(([DeliveryAddressId] IS NULL AND [DeliveryAddressVersion] IS NULL) OR ([DeliveryAddressId] IS NOT NULL AND [DeliveryAddressVersion] IS NOT NULL)) AND (([ContactId] IS NULL AND [ContactVersion] IS NULL) OR ([ContactId] IS NOT NULL AND [ContactVersion] IS NOT NULL)) "
                + "AND (([ResponsibleId] IS NULL AND [ResponsibleVersion] IS NULL) OR ([ResponsibleId] IS NOT NULL AND [ResponsibleVersion] IS NOT NULL)) AND (([PaymentTermsId] IS NULL AND [PaymentTermsVersion] IS NULL) OR ([PaymentTermsId] IS NOT NULL AND [PaymentTermsVersion] IS NOT NULL)) "
                + "AND (([SourceStrategyId] IS NULL AND [SourceStrategyVersion] IS NULL) OR ([SourceStrategyId] IS NOT NULL AND [SourceStrategyVersion] IS NOT NULL)) AND (([PackageId] IS NULL AND [PackageVersion] IS NULL) OR ([PackageId] IS NOT NULL AND [PackageVersion] IS NOT NULL))");
            // API-04: the sales owner is a real id when present; the due terms are JSON when present.
            table.HasCheckConstraint("CK_Drafts_HeaderRevision", HeaderRevisionConstraint);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.DocumentNo).HasMaxLength(SalesOrderDraft.MaximumDocumentNoLength).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.BusinessDate).HasColumnType("date");
        entity.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        entity.Property(x => x.Note).HasMaxLength(SalesOrderDraft.MaximumNoteLength);
        foreach (var version in new[]
                 {
                     nameof(DraftRow.DeliveryAddressVersion), nameof(DraftRow.ContactVersion), nameof(DraftRow.ResponsibleVersion),
                     nameof(DraftRow.PaymentTermsVersion), nameof(DraftRow.SourceStrategyVersion), nameof(DraftRow.PackageVersion),
                 })
        {
            entity.Property<string?>(version).HasMaxLength(128);
        }

        entity.Property(x => x.DueTermsJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.MoneyOperation).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MoneyProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MoneyEffectiveOn).HasColumnType("date");
        entity.Property(x => x.MoneyEffectiveFrom).HasColumnType("date");
        entity.Property(x => x.MoneyEffectiveThrough).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SubmittedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength);
        entity.Property(x => x.DecidedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength);
        entity.Property(x => x.CustomerAcceptedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength);
        entity.Property(x => x.RejectionReason).HasMaxLength(SalesOrderDraft.MaximumRejectionReasonLength);
        entity.HasIndex(x => new { x.CompanyId, x.ApprovalState, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Drafts_CompanyId_ApprovalState_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique().HasDatabaseName("UX_Drafts_DocumentNo");
        // No 162: one order per quote, and the source quote is this company's quote.
        entity.HasIndex(x => new { x.CompanyId, x.SourceQuoteId }).IsUnique().HasDatabaseName("UX_Drafts_SourceQuoteId")
            .HasFilter(SourceQuoteFilter);
        entity.HasOne<QuoteRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceQuoteId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Drafts_CompanyId_CreatedAtUtc_Id");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDraftLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftLineRow>();
        entity.ToTable("DraftLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_DraftLines_Identity",
                "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
            table.HasCheckConstraint("CK_DraftLines_Scale",
                "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
            table.HasCheckConstraint("CK_DraftLines_Price", "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
            // AT-018 / AT-021: the pricing detail exists only beside a price.
            table.HasCheckConstraint("CK_DraftLines_Pricing", "[PricingJson] IS NULL OR ([UnitPrice] IS NOT NULL AND ISJSON([PricingJson]) = 1)");
            table.HasCheckConstraint("CK_DraftLines_Window",
                "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
        });
        entity.HasKey(x => new { x.CompanyId, x.DraftId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.DraftId, x.LineId }).IsUnique().HasDatabaseName("UX_DraftLines_LineId");
        entity.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ProductCode).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ProductName).HasMaxLength(256).IsRequired();
        entity.Property(x => x.ProductEffectiveOn).HasColumnType("date");
        entity.Property(x => x.StockUnitProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SalesUnitProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OrderUnitBindingProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OrderUnitCode).HasMaxLength(64).IsRequired();
        entity.Property(x => x.OrderUnitDimensionCode).HasMaxLength(64).IsRequired();
        entity.Property(x => x.OrderUnitProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OrderUnitEffectiveFrom).HasColumnType("date");
        entity.Property(x => x.OrderUnitEffectiveThrough).HasColumnType("date");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        entity.Property(x => x.Note).HasMaxLength(SalesOrderDraft.MaximumNoteLength);
        entity.Property(x => x.UnitPrice).HasMaxLength(128);
        entity.Property(x => x.DiscountPercent).HasMaxLength(128);
        entity.Property(x => x.PricingJson).HasColumnType("nvarchar(max)");
        entity.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDraftDeliverySlices(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftDeliverySliceRow>();
        entity.ToTable("DraftDeliverySlices", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftDeliverySlices_SliceNo", "[SliceNo] >= 1");
            table.HasCheckConstraint("CK_DraftDeliverySlices_Identity",
                "[SliceId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftDeliverySlices_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.DraftId, x.LineNo, x.SliceNo });
        entity.HasIndex(x => new { x.CompanyId, x.DraftId, x.LineNo, x.SliceId }).IsUnique().HasDatabaseName("UX_DraftDeliverySlices_SliceId");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        entity.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.LineNo })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDraftAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftAttemptRow>();
        entity.ToTable("DraftAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftAttempts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_DraftAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            // ADR-011 §7: new mutation refusals bind to their draft; legacy null refusals remain replayable.
            table.HasCheckConstraint("CK_DraftAttempts_Outcome", AttemptOutcomeConstraint);
            table.HasCheckConstraint("CK_DraftAttempts_Result", AttemptResultConstraint);
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        entity.HasIndex(x => new { x.CompanyId, x.DraftId }).IsUnique().HasDatabaseName("UX_DraftAttempts_DraftId")
            .HasFilter(CreateAttemptFilter);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // API-22…24 (No 162, 163): the quote, its append-only revisions and lines, and the requests it covers.
    private void ConfigureQuotes(ModelBuilder modelBuilder)
    {
        var quote = modelBuilder.Entity<QuoteRow>();
        quote.ToTable("Quotes", Schema, table =>
        {
            table.HasCheckConstraint("CK_Quotes_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Quotes_State", "[State] IN (1, 2, 3) AND [Revision] >= 0");
            table.HasCheckConstraint("CK_Quotes_Lifecycle", QuoteLifecycleConstraint);
            table.HasCheckConstraint("CK_Quotes_Version", "[Version] >= [Revision] + 1");
            table.HasCheckConstraint("CK_Quotes_Text",
                "LEN([DocumentNo]) > 0 AND LEN([MoneyOperation]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Quotes_Money", "[MoneyScale] >= 0 AND ([MoneyEffectiveThrough] IS NULL OR [MoneyEffectiveThrough] >= [MoneyEffectiveFrom])");
        });
        quote.HasKey(x => x.Id);
        quote.HasAlternateKey(x => new { x.CompanyId, x.Id });
        quote.Property(x => x.Id).ValueGeneratedNever();
        quote.Property(x => x.DocumentNo).HasMaxLength(SalesQuote.MaximumDocumentNoLength).IsRequired();
        quote.Property(x => x.Version).IsConcurrencyToken();
        quote.Property(x => x.RowVersion).IsRowVersion();
        quote.Property(x => x.BusinessDate).HasColumnType("date");
        quote.Property(x => x.MoneyOperation).HasMaxLength(128).IsRequired();
        quote.Property(x => x.MoneyProfileVersion).HasMaxLength(128).IsRequired();
        quote.Property(x => x.MoneyEffectiveOn).HasColumnType("date");
        quote.Property(x => x.MoneyEffectiveFrom).HasColumnType("date");
        quote.Property(x => x.MoneyEffectiveThrough).HasColumnType("date");
        quote.Property(x => x.LossReason).HasMaxLength(SalesQuote.MaximumReasonLength);
        quote.Property(x => x.LossReasonCode).HasMaxLength(SalesQuote.MaximumLossReasonCodeLength);
        quote.Property(x => x.LossReasonLabel).HasMaxLength(SalesQuote.MaximumLossReasonLabelLength);
        quote.Property(x => x.LostBy).HasMaxLength(SalesQuote.MaximumActorLength);
        quote.Property(x => x.ConvertedBy).HasMaxLength(SalesQuote.MaximumActorLength);
        quote.Property(x => x.CreatedBy).HasMaxLength(SalesQuote.MaximumActorLength).IsRequired();
        quote.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        quote.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique().HasDatabaseName("UX_Quotes_DocumentNo");
        quote.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Quotes_CompanyId_CreatedAtUtc_Id");
        quote.HasIndex(x => new { x.CompanyId, x.State, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Quotes_CompanyId_State_CreatedAtUtc_Id");
        quote.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        quote.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var revision = modelBuilder.Entity<QuoteRevisionRow>();
        revision.ToTable("QuoteRevisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_QuoteRevisions_Identity",
                "[QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND LEN([RevisedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        revision.HasKey(x => new { x.CompanyId, x.QuoteId, x.Revision });
        revision.Property(x => x.ValidUntil).HasColumnType("date");
        revision.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        revision.Property(x => x.Note).HasMaxLength(SalesQuote.MaximumNoteLength);
        revision.Property(x => x.RevisedBy).HasMaxLength(SalesQuote.MaximumActorLength).IsRequired();
        revision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        revision.HasOne<QuoteRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.QuoteId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        revision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<QuoteLineRow>();
        line.ToTable("QuoteLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_QuoteLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_QuoteLines_Identity",
                "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_QuoteLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
            table.HasCheckConstraint("CK_QuoteLines_Scale",
                "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
            table.HasCheckConstraint("CK_QuoteLines_Price",
                "(([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)) AND ([DiscountPercent] IS NULL OR [UnitPrice] IS NOT NULL)");
            table.HasCheckConstraint("CK_QuoteLines_Window",
                "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
        });
        line.HasKey(x => new { x.CompanyId, x.QuoteId, x.Revision, x.LineNo });
        line.HasIndex(x => new { x.CompanyId, x.QuoteId, x.Revision, x.LineId }).IsUnique().HasDatabaseName("UX_QuoteLines_LineId");
        line.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.ProductCode).HasMaxLength(128).IsRequired();
        line.Property(x => x.ProductName).HasMaxLength(256).IsRequired();
        line.Property(x => x.ProductEffectiveOn).HasColumnType("date");
        line.Property(x => x.StockUnitProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.SalesUnitProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.OrderUnitBindingProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.OrderUnitCode).HasMaxLength(64).IsRequired();
        line.Property(x => x.OrderUnitDimensionCode).HasMaxLength(64).IsRequired();
        line.Property(x => x.OrderUnitProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.OrderUnitEffectiveFrom).HasColumnType("date");
        line.Property(x => x.OrderUnitEffectiveThrough).HasColumnType("date");
        line.Property(x => x.Quantity).HasPrecision(18, 3);
        line.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        line.Property(x => x.UnitPrice).HasMaxLength(128);
        line.Property(x => x.DiscountPercent).HasMaxLength(128);
        line.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        line.Property(x => x.Note).HasMaxLength(SalesQuote.MaximumNoteLength);
        line.HasOne<QuoteRevisionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.QuoteId, x.Revision })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var link = modelBuilder.Entity<QuoteRequestRow>();
        link.ToTable("QuoteRequests", Schema, table =>
        {
            table.HasCheckConstraint("CK_QuoteRequests_Identity",
                "[QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [RequestId] <> '00000000-0000-0000-0000-000000000000'");
        });
        link.HasKey(x => new { x.CompanyId, x.QuoteId, x.RequestId });
        link.HasIndex(x => new { x.CompanyId, x.RequestId }).IsUnique().HasDatabaseName("UX_QuoteRequests_ActiveRequest")
            .HasFilter(ActiveQuoteRequestFilter);
        link.HasOne<QuoteRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.QuoteId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        link.HasOne<RequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        link.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        // SAT-D24 / SAT-D44: the expired-quote approvals — one per (quote, revision), append-only.
        var approval = modelBuilder.Entity<QuoteExpiryApprovalRow>();
        approval.ToTable("QuoteExpiryApprovals", Schema, table =>
        {
            table.HasCheckConstraint("CK_QuoteExpiryApprovals_Identity",
                "[QuoteId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND LEN([ApprovedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_QuoteExpiryApprovals_Expired", "[ValidUntil] < [BusinessDay]");
            table.HasCheckConstraint("CK_QuoteExpiryApprovals_Note", "[Note] IS NULL OR LEN([Note]) > 0");
        });
        approval.HasKey(x => new { x.CompanyId, x.QuoteId, x.Revision });
        approval.Property(x => x.ValidUntil).HasColumnType("date");
        approval.Property(x => x.BusinessDay).HasColumnType("date");
        approval.Property(x => x.ApprovedBy).HasMaxLength(SalesQuote.MaximumActorLength).IsRequired();
        approval.Property(x => x.Note).HasMaxLength(SalesQuote.MaximumNoteLength);
        approval.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        approval.HasOne<QuoteRevisionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.QuoteId, x.Revision })
            .OnDelete(DeleteBehavior.Restrict);
        approval.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        // ADR-SAL-001 §2/§6: the quote commands' own attempt table, the draft attempt's shape, append-only in EF and in SQL.
        var attempt = modelBuilder.Entity<QuoteAttemptRow>();
        attempt.ToTable("QuoteAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_QuoteAttempts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_QuoteAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_QuoteAttempts_Command", QuoteAttemptCommandConstraint);
            table.HasCheckConstraint("CK_QuoteAttempts_Outcome", QuoteAttemptOutcomeConstraint);
            table.HasCheckConstraint("CK_QuoteAttempts_Result", QuoteAttemptResultConstraint);
            table.HasTrigger(QuoteAttemptImmutabilityTrigger);
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        attempt.HasIndex(x => new { x.CompanyId, x.QuoteId }).IsUnique().HasDatabaseName("UX_QuoteAttempts_QuoteId")
            .HasFilter(QuoteCreateAttemptFilter);
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<QuoteRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.QuoteId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // API-20 / API-21: the sales request, its lines and the display-number counter of the non-draft records.
    private void ConfigureRequests(ModelBuilder modelBuilder)
    {
        var counter = modelBuilder.Entity<DocumentCounterRow>();
        counter.ToTable("DocumentCounters", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentCounters_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Series]) > 0");
            table.HasCheckConstraint("CK_DocumentCounters_Next", "[Next] >= 1");
            table.HasCheckConstraint("CK_DocumentCounters_Version", "[Version] >= 1");
        });
        counter.HasKey(x => new { x.CompanyId, x.Series });
        counter.Property(x => x.Series).HasMaxLength(8).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        counter.Property(x => x.Version).IsConcurrencyToken();
        counter.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        counter.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var request = modelBuilder.Entity<RequestRow>();
        request.ToTable("Requests", Schema, table =>
        {
            table.HasCheckConstraint("CK_Requests_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Requests_State", "[State] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Requests_Decision", RequestDecisionConstraint);
            table.HasCheckConstraint("CK_Requests_Version", "[Version] >= 1 AND ([State] = 1 OR [Version] >= 2)");
            table.HasCheckConstraint("CK_Requests_Text",
                "LEN([DocumentNo]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        request.HasKey(x => x.Id);
        request.HasAlternateKey(x => new { x.CompanyId, x.Id });
        request.Property(x => x.Id).ValueGeneratedNever();
        request.Property(x => x.DocumentNo).HasMaxLength(SalesRequest.MaximumDocumentNoLength).IsRequired();
        request.Property(x => x.Version).IsConcurrencyToken();
        request.Property(x => x.RowVersion).IsRowVersion();
        request.Property(x => x.DecidedBy).HasMaxLength(SalesRequest.MaximumActorLength);
        request.Property(x => x.RejectionReason).HasMaxLength(SalesRequest.MaximumReasonLength);
        request.Property(x => x.BusinessDate).HasColumnType("date");
        request.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        request.Property(x => x.Note).HasMaxLength(SalesRequest.MaximumNoteLength);
        request.Property(x => x.CreatedBy).HasMaxLength(SalesRequest.MaximumActorLength).IsRequired();
        request.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        request.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique().HasDatabaseName("UX_Requests_DocumentNo");
        request.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Requests_CompanyId_CreatedAtUtc_Id");
        request.HasIndex(x => new { x.CompanyId, x.State, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Requests_CompanyId_State_CreatedAtUtc_Id");
        request.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        request.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<RequestLineRow>();
        line.ToTable("RequestLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_RequestLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_RequestLines_Identity",
                "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_RequestLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
            table.HasCheckConstraint("CK_RequestLines_Scale",
                "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
            table.HasCheckConstraint("CK_RequestLines_Window",
                "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
        });
        line.HasKey(x => new { x.CompanyId, x.RequestId, x.LineNo });
        line.HasIndex(x => new { x.CompanyId, x.RequestId, x.LineId }).IsUnique().HasDatabaseName("UX_RequestLines_LineId");
        line.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.ProductCode).HasMaxLength(128).IsRequired();
        line.Property(x => x.ProductName).HasMaxLength(256).IsRequired();
        line.Property(x => x.ProductEffectiveOn).HasColumnType("date");
        line.Property(x => x.StockUnitProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.SalesUnitProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.OrderUnitBindingProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.OrderUnitCode).HasMaxLength(64).IsRequired();
        line.Property(x => x.OrderUnitDimensionCode).HasMaxLength(64).IsRequired();
        line.Property(x => x.OrderUnitProfileVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.OrderUnitEffectiveFrom).HasColumnType("date");
        line.Property(x => x.OrderUnitEffectiveThrough).HasColumnType("date");
        line.Property(x => x.Quantity).HasPrecision(18, 3);
        line.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        line.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        line.Property(x => x.Note).HasMaxLength(SalesRequest.MaximumNoteLength);
        line.HasOne<RequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        // ADR-SAL-001 §2/§6: the request commands' own attempt table, the draft attempt's shape, append-only in EF and in SQL.
        var attempt = modelBuilder.Entity<RequestAttemptRow>();
        attempt.ToTable("RequestAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_RequestAttempts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_RequestAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_RequestAttempts_Command", RequestAttemptCommandConstraint);
            table.HasCheckConstraint("CK_RequestAttempts_Outcome", RequestAttemptOutcomeConstraint);
            table.HasCheckConstraint("CK_RequestAttempts_Result", RequestAttemptResultConstraint);
            table.HasTrigger(RequestAttemptImmutabilityTrigger);
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        attempt.HasIndex(x => new { x.CompanyId, x.RequestId }).IsUnique().HasDatabaseName("UX_RequestAttempts_RequestId")
            .HasFilter(RequestCreateAttemptFilter);
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<RequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // DEC-SAL-003: the recorded delivery-schedule changes and their dates — append-only history beside the draft.
    private void ConfigureDraftDeliverySchedules(ModelBuilder modelBuilder)
    {
        var schedule = modelBuilder.Entity<DraftDeliveryScheduleRow>();
        schedule.ToTable("DraftDeliverySchedules", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftDeliverySchedules_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ScheduleNo] >= 1 AND [DraftVersion] >= 2 AND LEN([ChangedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        schedule.HasKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo });
        schedule.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        schedule.Property(x => x.ChangedBy).HasMaxLength(128).IsRequired();
        schedule.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        schedule.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        schedule.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var date = modelBuilder.Entity<DraftDeliveryScheduleDateRow>();
        date.ToTable("DraftDeliveryScheduleDates", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftDeliveryScheduleDates_Position", "[LineNo] >= 1 AND [SliceNo] >= 0");
            table.HasCheckConstraint("CK_DraftDeliveryScheduleDates_Slice",
                "([SliceNo] = 0 AND [SliceId] IS NULL) "
                + "OR ([SliceNo] >= 1 AND [SliceId] IS NOT NULL AND [SliceId] <> '00000000-0000-0000-0000-000000000000' AND [RequestedDeliveryDate] IS NOT NULL)");
        });
        date.HasKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo, x.LineNo, x.SliceNo });
        date.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        date.HasOne<DraftDeliveryScheduleRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo })
            .HasPrincipalKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo })
            .OnDelete(DeleteBehavior.Restrict);
        date.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.LineNo })
            .OnDelete(DeleteBehavior.Restrict);
        date.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // SAT-D115 (EK14): the recorded customer rejections — append-only history beside the draft, at most one per revision.
    private void ConfigureDraftCustomerRejections(ModelBuilder modelBuilder)
    {
        var rejection = modelBuilder.Entity<DraftCustomerRejectionRow>();
        rejection.ToTable("DraftCustomerRejections", Schema, table =>
        {
            // A rejected revision is never Rev.0 (an earlier accepted one exists); the smallest version a rejection can produce is
            // create (1), one line (2), submit (3), approve (4), accept (5), revise (6), submit (7), approve (8), reject (9).
            table.HasCheckConstraint("CK_DraftCustomerRejections_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [Revision] >= 1 AND [DraftVersion] >= 9 AND LEN([RejectedBy]) > 0 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0");
        });
        rejection.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision });
        rejection.Property(x => x.RejectedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        rejection.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumCustomerRejectionReasonLength).IsRequired();
        rejection.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        rejection.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        rejection.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // DEC-SAL-005 / AT-007: the recorded cancellation — append-only history beside the draft, at most one per draft.
    private void ConfigureDraftCancellations(ModelBuilder modelBuilder)
    {
        var cancellation = modelBuilder.Entity<DraftCancellationRow>();
        cancellation.ToTable("DraftCancellations", Schema, table =>
        {
            // The smallest version a cancellation can produce: create (1), one line (2), submit (3), approve (4), cancel (5).
            table.HasCheckConstraint("CK_DraftCancellations_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [Revision] >= 0 AND [DraftVersion] >= 5 AND LEN([CancelledBy]) > 0 AND LEN([Reason]) > 0 "
                + "AND LEN([ImpactToken]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftCancellations_Impact", "ISJSON([ImpactJson]) = 1");
        });
        cancellation.HasKey(x => new { x.CompanyId, x.DraftId });
        cancellation.Property(x => x.CancelledBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        cancellation.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumCancellationReasonLength).IsRequired();
        cancellation.Property(x => x.ImpactToken).HasMaxLength(128).IsRequired();
        cancellation.Property(x => x.ImpactJson).HasColumnType("nvarchar(max)").IsRequired();
        cancellation.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        cancellation.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        cancellation.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // No 30 / SAT-D12: the recorded force-closure — append-only history beside the draft, at most one per draft; the impact the
    // user confirmed and every line's closed remainder as JSON.
    private void ConfigureDraftForceClosures(ModelBuilder modelBuilder)
    {
        var closure = modelBuilder.Entity<DraftForceClosureRow>();
        closure.ToTable("DraftForceClosures", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftForceClosures_Identity", ForceClosureIdentityConstraint);
            table.HasCheckConstraint("CK_DraftForceClosures_Impact", "ISJSON([ImpactJson]) = 1");
            table.HasCheckConstraint("CK_DraftForceClosures_Lines", "ISJSON([LinesJson]) = 1");
        });
        closure.HasKey(x => new { x.CompanyId, x.DraftId });
        closure.Property(x => x.ClosedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        closure.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumForceCloseReasonLength).IsRequired();
        closure.Property(x => x.ImpactToken).HasMaxLength(128).IsRequired();
        closure.Property(x => x.ImpactJson).HasColumnType("nvarchar(max)").IsRequired();
        closure.Property(x => x.LinesJson).HasColumnType("nvarchar(max)").IsRequired();
        closure.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        closure.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        closure.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // Partial delivery (user decision 2026-09-30): the remainder changes and their decisions — append-only history beside the
    // draft. The line's approved quantity lives on the line row (the quantity revision's writable columns).
    private void ConfigureDraftRemainderChanges(ModelBuilder modelBuilder)
    {
        var change = modelBuilder.Entity<DraftRemainderChangeRow>();
        change.ToTable("DraftRemainderChanges", Schema, table =>
        {
            // The smallest version a request can produce: create (1), one line (2), submit (3), approve (4), request (5).
            table.HasCheckConstraint("CK_DraftRemainderChanges_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ChangeNo] >= 1 AND [Revision] >= 0 AND [LineNo] >= 1 "
                + "AND [DraftVersion] >= 5 AND LEN([OrderedQuantityText]) > 0 AND LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftRemainderChanges_Kind", RemainderKindConstraint);
            table.HasCheckConstraint("CK_DraftRemainderChanges_Floor", RemainderFloorConstraint);
        });
        change.HasKey(x => new { x.CompanyId, x.DraftId, x.ChangeNo });
        change.HasIndex(x => new { x.CompanyId, x.DraftId, x.DraftVersion }).IsUnique().HasDatabaseName("UX_DraftRemainderChanges_DraftVersion");
        change.Property(x => x.OrderedQuantityText).HasMaxLength(128).IsRequired();
        change.Property(x => x.RequestedQuantity).HasPrecision(18, 3);
        change.Property(x => x.RequestedQuantityText).HasMaxLength(128);
        change.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumRemainderReasonLength).IsRequired();
        change.Property(x => x.RequestedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        change.Property(x => x.LoadedQuantity).HasPrecision(18, 3);
        change.Property(x => x.ShippedQuantity).HasPrecision(18, 3);
        change.Property(x => x.DeliveredQuantity).HasPrecision(18, 3);
        change.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        change.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        change.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.LineNo })
            .OnDelete(DeleteBehavior.Restrict);
        change.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var decision = modelBuilder.Entity<DraftRemainderDecisionRow>();
        decision.ToTable("DraftRemainderDecisions", Schema, table =>
        {
            // The smallest version a decision can produce: the request's (5) plus the decision (6).
            table.HasCheckConstraint("CK_DraftRemainderDecisions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ChangeNo] >= 1 AND [DraftVersion] >= 6 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftRemainderDecisions_Outcome", RemainderOutcomeConstraint);
        });
        decision.HasKey(x => new { x.CompanyId, x.DraftId, x.ChangeNo });
        decision.HasIndex(x => new { x.CompanyId, x.DraftId, x.DraftVersion }).IsUnique().HasDatabaseName("UX_DraftRemainderDecisions_DraftVersion");
        decision.Property(x => x.DecidedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        decision.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumRemainderReasonLength);
        decision.Property(x => x.ResultQuantity).HasPrecision(18, 3);
        decision.Property(x => x.ResultQuantityText).HasMaxLength(128);
        decision.Property(x => x.LoadedQuantity).HasPrecision(18, 3);
        decision.Property(x => x.ShippedQuantity).HasPrecision(18, 3);
        decision.Property(x => x.DeliveredQuantity).HasPrecision(18, 3);
        decision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        decision.HasOne<DraftRemainderChangeRow>().WithOne()
            .HasForeignKey<DraftRemainderDecisionRow>(x => new { x.CompanyId, x.DraftId, x.ChangeNo })
            .HasPrincipalKey<DraftRemainderChangeRow>(x => new { x.CompanyId, x.DraftId, x.ChangeNo })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // K3 Head B: the archived approved revisions and their lines — append-only history beside the draft.
    private void ConfigureDraftRevisions(ModelBuilder modelBuilder)
    {
        var revision = modelBuilder.Entity<DraftRevisionRow>();
        revision.ToTable("DraftRevisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftRevisions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [Revision] >= 0 AND LEN([SubmittedBy]) > 0 AND LEN([ApprovedBy]) > 0 AND LEN([ArchivedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftRevisions_Money",
                "[MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND LEN([MoneyProfileVersion]) > 0 AND [MoneyScale] >= 0");
            // DEC-SAL-002: a quantity revision's reason is non-blank; a price revision archives with none (AddRevisionReasons).
            table.HasCheckConstraint("CK_DraftRevisions_Reason", "[Reason] IS NULL OR LEN([Reason]) > 0");
        });
        revision.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision });
        revision.Property(x => x.SubmittedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.ApprovedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.ArchivedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        revision.Property(x => x.MoneyProfileVersion).HasMaxLength(128).IsRequired();
        revision.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumRevisionReasonLength);
        revision.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        revision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<DraftRevisionLineRow>();
        line.ToTable("DraftRevisionLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftRevisionLines_Identity",
                "[LineNo] >= 1 AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND LEN([QuantityText]) > 0");
            table.HasCheckConstraint("CK_DraftRevisionLines_Price",
                "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
            table.HasCheckConstraint("CK_DraftRevisionLines_Pricing",
                "[PricingJson] IS NULL OR ([UnitPrice] IS NOT NULL AND ISJSON([PricingJson]) = 1)");
        });
        line.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision, x.LineNo });
        line.HasIndex(x => new { x.CompanyId, x.DraftId, x.Revision, x.LineId }).IsUnique().HasDatabaseName("UX_DraftRevisionLines_LineId");
        line.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        line.Property(x => x.UnitPrice).HasMaxLength(128);
        line.Property(x => x.DiscountPercent).HasMaxLength(128);
        line.Property(x => x.PricingJson).HasColumnType("nvarchar(max)");
        line.HasOne<DraftRevisionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.Revision })
            .HasPrincipalKey(x => new { x.CompanyId, x.DraftId, x.Revision })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        // API-04: the header an archived approved revision was approved with — one row per archive, append-only.
        var header = modelBuilder.Entity<DraftRevisionHeaderRow>();
        header.ToTable("DraftRevisionHeaders", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftRevisionHeaders_Identity",
                "[PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftRevisionHeaders_References",
                "(([DeliveryAddressId] IS NULL AND [DeliveryAddressVersion] IS NULL) OR ([DeliveryAddressId] IS NOT NULL AND [DeliveryAddressVersion] IS NOT NULL)) "
                + "AND (([ContactId] IS NULL AND [ContactVersion] IS NULL) OR ([ContactId] IS NOT NULL AND [ContactVersion] IS NOT NULL))");
            table.HasCheckConstraint("CK_DraftRevisionHeaders_HeaderRevision", HeaderRevisionConstraint);
        });
        header.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision });
        header.Property(x => x.DeliveryAddressVersion).HasMaxLength(128);
        header.Property(x => x.ContactVersion).HasMaxLength(128);
        header.Property(x => x.DueTermsJson).HasColumnType("nvarchar(max)");
        header.HasOne<DraftRevisionRow>().WithOne()
            .HasForeignKey<DraftRevisionHeaderRow>(x => new { x.CompanyId, x.DraftId, x.Revision })
            .HasPrincipalKey<DraftRevisionRow>(x => new { x.CompanyId, x.DraftId, x.Revision })
            .OnDelete(DeleteBehavior.Restrict);
        header.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>API-04: a present sales owner is a real id; present due terms are JSON (the shape is the store's strict reader's).</summary>
    private const string HeaderRevisionConstraint =
        "([SalesOwnerUserId] IS NULL OR [SalesOwnerUserId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND ([DueTermsJson] IS NULL OR ISJSON([DueTermsJson]) = 1)";

    private void ApplyWriteGuards()
    {
        SalesAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<ISalesTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Satış taslağı kayıtları silinmez; değişiklik yeni bir revizyondur (T33).");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Satış kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Satış kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    DraftNumberRow => [nameof(DraftNumberRow.Next), nameof(DraftNumberRow.UpdatedAtUtc)],
                    // SALES-A1: the header moves only along the approval axis (state, facts, time; the version is the token);
                    // K3 Head B: and its commercial revision number, which the archived history explains; DEC-SAL-005: and its
                    // lifecycle State, Draft → Cancelled only (checked below), which the appended cancellation row explains; No 30:
                    // or Draft → Closed, which the appended force-closure row explains.
                    DraftRow =>
                    [
                        nameof(DraftRow.UpdatedAtUtc), nameof(DraftRow.ApprovalState), nameof(DraftRow.SubmittedBy), nameof(DraftRow.SubmittedAtUtc),
                        nameof(DraftRow.DecidedBy), nameof(DraftRow.DecidedAtUtc), nameof(DraftRow.RejectionReason), nameof(DraftRow.Revision),
                        nameof(DraftRow.State), nameof(DraftRow.CustomerAcceptedRevision), nameof(DraftRow.CustomerAcceptedBy),
                        nameof(DraftRow.CustomerAcceptedAtUtc),
                        // API-04 (DEC-SAL-002) — a DELIBERATE widening of "the header moves only along its axes", declared here
                        // and in the PR: the header revision moves the customer, the currency with its money profile, the
                        // delivery references, the sales owner and the due terms (the approved values stay in the archived
                        // revision's header row). The business date, note, document number, creator and the other owner
                        // references stay immutable (the persistence test pins both halves).
                        nameof(DraftRow.PartyId), nameof(DraftRow.CompanyAccountId), nameof(DraftRow.CurrencyId),
                        nameof(DraftRow.MoneyOperation), nameof(DraftRow.MoneyProfileId), nameof(DraftRow.MoneyProfileVersion),
                        nameof(DraftRow.MoneyEffectiveOn), nameof(DraftRow.MoneyIsActive), nameof(DraftRow.MoneyEffectiveFrom),
                        nameof(DraftRow.MoneyEffectiveThrough), nameof(DraftRow.MoneyScale),
                        nameof(DraftRow.DeliveryAddressId), nameof(DraftRow.DeliveryAddressVersion), nameof(DraftRow.ContactId),
                        nameof(DraftRow.ContactVersion), nameof(DraftRow.SalesOwnerUserId), nameof(DraftRow.DueTermsJson),
                    ],
                    // K3 Head B — a DELIBERATE narrowing of "lines never change", declared here and in the PR: ONLY the three
                    // commercial columns may change, under a commercial revision; DEC-SAL-002 widens it by exactly the ordered
                    // quantity and its verbatim text, under a quantity revision (the approved values stay in the archived
                    // revision's lines). Identity, product, unit and slice columns stay immutable (the persistence test pins both halves).
                    DraftLineRow =>
                    [
                        nameof(DraftLineRow.UnitPrice), nameof(DraftLineRow.PriceUnitId), nameof(DraftLineRow.DiscountPercent),
                        // AT-018 / AT-021: the pricing detail moves with the three commercial columns, under a commercial revision.
                        nameof(DraftLineRow.PricingJson),
                        nameof(DraftLineRow.Quantity), nameof(DraftLineRow.QuantityText),
                    ],
                    // DEC-SAL-003: the delivery schedule is APPEND-ONLY — a change adds a schedule and its dates and moves
                    // only the header's version (the token) and UpdatedAtUtc, both already admitted above. Nothing of an
                    // added schedule may change afterwards: no column is admitted.
                    DraftDeliveryScheduleRow => [],
                    DraftDeliveryScheduleDateRow => [],
                    // API-04: an archived revision's header is APPEND-ONLY — no column may change afterwards.
                    DraftRevisionHeaderRow => [],
                    // DEC-SAL-005: a recorded cancellation is APPEND-ONLY — no column of it may change afterwards.
                    DraftCancellationRow => [],
                    // SAT-D115: a recorded customer rejection is APPEND-ONLY — no column of it may change afterwards.
                    DraftCustomerRejectionRow => [],
                    // No 30 / SAT-D12: a recorded force-closure is APPEND-ONLY — no column of it may change afterwards.
                    DraftForceClosureRow => [],
                    // Partial delivery: a remainder request and its decision are APPEND-ONLY — no column may change afterwards.
                    DraftRemainderChangeRow => [],
                    DraftRemainderDecisionRow => [],
                    // API-20: a counter moves its counter only (the version is its token).
                    DocumentCounterRow => [nameof(DocumentCounterRow.Next), nameof(DocumentCounterRow.UpdatedAtUtc)],
                    // API-21: a request moves only along its decision; its lines never change.
                    RequestRow =>
                    [
                        nameof(RequestRow.State), nameof(RequestRow.DecidedBy), nameof(RequestRow.DecidedAtUtc),
                        nameof(RequestRow.RejectionReason), nameof(RequestRow.UpdatedAtUtc),
                    ],
                    RequestLineRow => [],
                    // API-22…24: a quote moves only along its lifecycle (state, current revision, loss, conversion); its
                    // revisions and lines are append-only; a covered request's link only deactivates (the quote was lost).
                    QuoteRow =>
                    [
                        nameof(QuoteRow.State), nameof(QuoteRow.Revision), nameof(QuoteRow.LossReason), nameof(QuoteRow.LostBy),
                        nameof(QuoteRow.LostAtUtc), nameof(QuoteRow.ConvertedDraftId), nameof(QuoteRow.ConvertedBy),
                        nameof(QuoteRow.ConvertedAtUtc), nameof(QuoteRow.UpdatedAtUtc),
                        // SAT-D25: the chosen list entry's snapshot moves with the loss (Open → Lost only, checked below).
                        nameof(QuoteRow.LossReasonId), nameof(QuoteRow.LossReasonCode), nameof(QuoteRow.LossReasonLabel),
                        nameof(QuoteRow.LossReasonProfileId), nameof(QuoteRow.LossReasonProfileVersion),
                    ],
                    QuoteRevisionRow => [],
                    // SAT-D24 / SAT-D44: a recorded expired-quote approval is APPEND-ONLY — no column may change afterwards.
                    QuoteExpiryApprovalRow => [],
                    QuoteLineRow => [],
                    QuoteRequestRow => [nameof(QuoteRequestRow.IsActive)],
                    // ADR-SAL-001 §2: a quote attempt is APPEND-ONLY (the SQL trigger refuses the same).
                    QuoteAttemptRow => [],
                    // ADR-SAL-001 §2: a request attempt is APPEND-ONLY (the SQL trigger refuses the same).
                    RequestAttemptRow => [],
                    _ => [],
                };
                if (entry.Entity is RequestRow)
                {
                    // API-21: a decision is taken once — Submitted → Approved | Rejected, Approved → Rejected; nothing else.
                    var requestState = entry.Property(nameof(RequestRow.State));
                    if (requestState.IsModified && !((int)requestState.OriginalValue! == (int)SalesRequestState.Submitted
                            && (int)requestState.CurrentValue! is (int)SalesRequestState.Approved or (int)SalesRequestState.Rejected
                        || (int)requestState.OriginalValue! == (int)SalesRequestState.Approved
                            && (int)requestState.CurrentValue! == (int)SalesRequestState.Rejected))
                    {
                        throw new InvalidOperationException("Satış talebinin durumu yalnız Onay bekliyor → Onaylı/Reddedildi ya da Onaylı → Reddedildi yönünde değişir.");
                    }
                }

                if (entry.Entity is QuoteRow)
                {
                    // API-22…24: Open → Converted | Lost only; a revision moves the revision counter forward on an open quote.
                    var quoteState = entry.Property(nameof(QuoteRow.State));
                    if (quoteState.IsModified && !((int)quoteState.OriginalValue! == (int)SalesQuoteState.Open
                            && (int)quoteState.CurrentValue! is (int)SalesQuoteState.Converted or (int)SalesQuoteState.Lost))
                    {
                        throw new InvalidOperationException("Teklifin durumu yalnız Açık → Dönüştürüldü/Kaybedildi yönünde değişir.");
                    }

                    var revision = entry.Property(nameof(QuoteRow.Revision));
                    if (revision.IsModified && (int)revision.CurrentValue! <= (int)revision.OriginalValue!)
                    {
                        throw new InvalidOperationException("Teklif revizyonu yalnız ileri gider.");
                    }
                }

                if (entry.Entity is QuoteRequestRow)
                {
                    var active = entry.Property(nameof(QuoteRequestRow.IsActive));
                    if (active.IsModified && !((bool)active.OriginalValue! && !(bool)active.CurrentValue!))
                    {
                        throw new InvalidOperationException("Teklifin talep bağı yalnız etkinden pasife döner.");
                    }
                }

                if (entry.Entity is DraftRow)
                {
                    var state = entry.Property(nameof(DraftRow.State));
                    if (state.IsModified && !((int)state.OriginalValue! == (int)SalesDraftState.Draft
                            && (int)state.CurrentValue! is (int)SalesDraftState.Cancelled or (int)SalesDraftState.Closed))
                    {
                        throw new InvalidOperationException("Satış siparişinin yaşam döngüsü yalnız Taslak → İptal ya da Taslak → Kapalı yönünde değişir.");
                    }

                    var acceptedRevision = entry.Property(nameof(DraftRow.CustomerAcceptedRevision));
                    var acceptedBy = entry.Property(nameof(DraftRow.CustomerAcceptedBy));
                    var acceptedAt = entry.Property(nameof(DraftRow.CustomerAcceptedAtUtc));
                    if (acceptedRevision.IsModified || acceptedBy.IsModified || acceptedAt.IsModified)
                    {
                        var required = entry.Property(nameof(DraftRow.CustomerAcceptanceRequired));
                        var approval = entry.Property(nameof(DraftRow.ApprovalState));
                        var revision = entry.Property(nameof(DraftRow.Revision));
                        var currentAcceptedRevision = (int?)acceptedRevision.CurrentValue;
                        var originalAcceptedRevision = (int?)acceptedRevision.OriginalValue;
                        if (required.IsModified || required.CurrentValue is not true || approval.IsModified
                            || approval.CurrentValue is not (int)InternalApprovalState.Approved || revision.IsModified
                            || currentAcceptedRevision is null || currentAcceptedRevision != (int)revision.CurrentValue!
                            || originalAcceptedRevision is { } previous && previous >= currentAcceptedRevision
                            || acceptedBy.CurrentValue is not string actor || string.IsNullOrWhiteSpace(actor)
                            || acceptedAt.CurrentValue is not DateTimeOffset)
                        {
                            throw new InvalidOperationException("Müşteri kabulü yalnız onaylı güncel revizyona ileri bir geçiş olarak kaydedilir.");
                        }
                    }
                }

                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Satış kaydının yalnız izinli alanı değişir; satırlar, dilimler ve deneme kayıtları değişmez.");
                }
            }
        }
    }
}
