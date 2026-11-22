using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// Purchasing's own tables in the <c>purchasing</c> schema (PUR-4a; ADR-002, ADR-009, ADR-011; the Sales
/// SALES-P2 / Logistics LOG-4a shape). Every row is a tenant row of the record's company: a global query
/// filter on the server-resolved company and a write guard that refuses any row of another company, any
/// company change, any delete and any column move outside the axis a command may move (a header's status /
/// version / time, a request's sourcing version; an order line's technical, accepted, (A1-3) cancelled and (API-11) closed totals;
/// attempts, (A3 API-30) delivery-slice revisions, (API-11) line-closure events and outbox messages append only). The attempt key (company, command, key hash) is a primary key under a binary collation; the raw key
/// is never stored. The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// Nothing here is physical: an approved order is expected supply, never stock.
/// </summary>
public sealed partial class PurchasingDbContext(DbContextOptions<PurchasingDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "purchasing";
    public const string IdentityCollation = "Latin1_General_100_BIN2";
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    /// <summary>One create attempt per stored request; the submit command's attempts share the request id (the Sales lesson).</summary>
    internal static readonly string RequestCreateAttemptFilter =
        "[RequestId] IS NOT NULL AND [CommandName] = N'" + PurchasingCommands.RequestCreateV1 + "'";

    /// <summary>
    /// A1-1: Draft has no decision fact; Submitted may lack its submitter only for a submission recorded before A1-1 (the
    /// domain then refuses every decision, fail-closed); Approved has its submitter and approval and no rejection; Rejected
    /// has its submitter, rejection and reason, and keeps an earlier approval if there was one. A1-1b: Withdrawn (5) has no
    /// approval or rejection, and its submitter only if one was recorded (the preparer may withdraw a request submitted
    /// before A1-1, ALS-D47); its own facts are <see cref="RequestWithdrawalCheck"/>'s.
    /// </summary>
    internal const string RequestDecisionCheck =
        "([Status] = 1 AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL "
        + "AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([Status] = 2 AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL "
        + "AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([Status] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 "
        + "AND [ApprovedAtUtc] IS NOT NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([Status] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [RejectedBy] IS NOT NULL AND LEN([RejectedBy]) > 0 "
        + "AND [RejectedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 "
        + "AND (([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL))) "
        + "OR ([Status] = 5 AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL "
        + "AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL)";

    /// <summary>
    /// A1-1 (ALS-D2): neither the approver nor the rejecter is the preparer (CreatedBy) or the submitter. A1-1b (#900 review
    /// P3): the comparison is binary (<see cref="IdentityCollation"/>), the domain's ordinal rule — under the database's
    /// case-insensitive default a decider differing from a maker only by case passed the domain and failed this check.
    /// </summary>
    internal const string RequestMakerCheckerCheck =
        "([ApprovedBy] IS NULL OR ([ApprovedBy] COLLATE " + IdentityCollation + " <> [CreatedBy] COLLATE " + IdentityCollation
        + " AND [SubmittedBy] IS NOT NULL AND [ApprovedBy] COLLATE " + IdentityCollation + " <> [SubmittedBy] COLLATE " + IdentityCollation + ")) "
        + "AND ([RejectedBy] IS NULL OR ([RejectedBy] COLLATE " + IdentityCollation + " <> [CreatedBy] COLLATE " + IdentityCollation
        + " AND [SubmittedBy] IS NOT NULL AND [RejectedBy] COLLATE " + IdentityCollation + " <> [SubmittedBy] COLLATE " + IdentityCollation + "))";

    /// <summary>
    /// A1-1b (ALS-D46, ALS-D47): a Withdrawn request has its withdrawer (exactly the preparer or the recorded submitter,
    /// binary comparison), the time and a non-blank reason; any other status has none of the three.
    /// </summary>
    internal const string RequestWithdrawalCheck =
        "([Status] = 5 AND [WithdrawnBy] IS NOT NULL AND LEN([WithdrawnBy]) > 0 AND [WithdrawnAtUtc] IS NOT NULL "
        + "AND [WithdrawalReason] IS NOT NULL AND LEN([WithdrawalReason]) > 0 "
        + "AND ([WithdrawnBy] COLLATE " + IdentityCollation + " = [CreatedBy] COLLATE " + IdentityCollation
        + " OR ([SubmittedBy] IS NOT NULL AND [WithdrawnBy] COLLATE " + IdentityCollation + " = [SubmittedBy] COLLATE " + IdentityCollation + "))) "
        + "OR ([Status] <> 5 AND [WithdrawnBy] IS NULL AND [WithdrawnAtUtc] IS NULL AND [WithdrawalReason] IS NULL)";

    /// <summary>
    /// A1-2 (plan API-02): a request line's source is either absent (manual: kind and both identities null) or a defined kind
    /// (1 Stock, 2 Sales, 3 Production) with both identities present and non-empty. A CHECK passes on UNKNOWN, so every
    /// column is tested with IS [NOT] NULL.
    /// </summary>
    internal const string RequestLineSourceCheck =
        "([SourceKind] IS NULL AND [SourceDocumentId] IS NULL AND [SourceDocumentLineId] IS NULL) "
        + "OR ([SourceKind] IS NOT NULL AND [SourceKind] IN (1, 2, 3) AND [SourceDocumentId] IS NOT NULL AND [SourceDocumentId] <> " + EmptyGuid
        + " AND [SourceDocumentLineId] IS NOT NULL AND [SourceDocumentLineId] <> " + EmptyGuid + ")";

    /// <summary>
    /// A1-3 (plan API-10, No.161 §2): a Cancelled (3) order has its canceller, the time and a non-blank reason; any other status
    /// has none of the three. A CHECK passes on UNKNOWN, so every fact is tested with IS [NOT] NULL.
    /// </summary>
    internal const string OrderCancellationCheck =
        "([Status] = 3 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL "
        + "AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) "
        + "OR ([Status] = 2 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL "
        + "AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) "
        + "OR ([Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL)";

    /// <summary>
    /// A2a-5a (ALS-D3, Fable F-015): the approver and the approval time come together; only an Approved (2) or Cancelled (3)
    /// order carries them, and the approver is never the preparer (binary comparison, the domain's ordinal rule). An order
    /// approved before A2a-5a keeps both null (its approver was not recorded). A CHECK passes on UNKNOWN, so every fact is
    /// tested with IS [NOT] NULL.
    /// </summary>
    internal const string OrderApprovalCheck =
        "([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) "
        + "OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [Status] IN (2, 3) "
        + "AND [ApprovedBy] COLLATE " + IdentityCollation + " <> COALESCE([CurrentPreparedBy], [CreatedBy]) COLLATE " + IdentityCollation + ")";

    /// <summary>
    /// §7.2 / §13, A1-3 and API-11 (master INV-006): technical slices, the accepted share, the cancelled remainder and the closed
    /// remainder are never negative and together never exceed the first commitment.
    /// </summary>
    internal const string OrderLineSharesCheck =
        "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Cancelled] >= 0 AND [Closed] >= 0 AND [DefinitiveReturned] >= 0 "
        + "AND [Technical] + [Accepted] + [Cancelled] + [Closed] + [DefinitiveReturned] <= [Ordered]";

    /// <summary>
    /// API-11 (F-017 §3.6): a remainder is either cancelled with the order or closed on the line, never both — closing refuses a
    /// cancelled order and cancelling counts only the remainder still open (the domain's rule, held here as defence in depth).
    /// </summary>
    // ALS-D92: historical closed and cancelled shares may coexist after a net receipt revival.
    // The exact aggregate shares CHECK, not mutual exclusion, prevents reopening twice.
    internal const string OrderLineClosedOrCancelledCheck = "[Closed] >= 0 AND [Cancelled] >= 0";

    /// <summary>The outbox's message types (the store writes nothing else).</summary>
    internal static readonly string OutboxEventTypeCheck =
        "[EventType] IN ('" + PurchasingOrderLineClosedEventV1.EventType + "', '" + PurchasingOrderLineRevivedEventV1.EventType
        + "', '" + PurchaseInvoiceFinalizedEventV1.EventType + "')";

    /// <summary>
    /// A2a-1 (TEK-01): the order's money snapshot is all five facts or none — a non-empty currency and profile, a non-blank code, a
    /// version from 1 and a scale within System.Decimal's 0..28. A CHECK passes on UNKNOWN, so every column is tested with IS [NOT] NULL.
    /// </summary>
    internal const string OrderMoneyCheck =
        "([CurrencyId] IS NULL AND [CurrencyCode] IS NULL AND [MoneyProfileId] IS NULL AND [MoneyProfileVersion] IS NULL AND [MoneyScale] IS NULL) "
        + "OR ([CurrencyId] IS NOT NULL AND [CurrencyId] <> " + EmptyGuid + " AND [CurrencyCode] IS NOT NULL AND LEN([CurrencyCode]) > 0 "
        + "AND [MoneyProfileId] IS NOT NULL AND [MoneyProfileId] <> " + EmptyGuid + " AND [MoneyProfileVersion] IS NOT NULL AND [MoneyProfileVersion] >= 1 "
        + "AND [MoneyScale] IS NOT NULL AND [MoneyScale] >= 0 AND [MoneyScale] <= 28)";

    /// <summary>
    /// A2a-1c (ALS-D72): the order's unit-price scale is absent, or present only beside a money snapshot and within System.Decimal's
    /// 0..28. It is kept apart from <see cref="OrderMoneyCheck"/>, which stays as A2a-1 wrote it: a legacy priced order has no
    /// unit-price scale and keeps the amount scale for its unit prices.
    /// </summary>
    internal const string OrderUnitPriceScaleCheck =
        "[UnitPriceScale] IS NULL OR ([CurrencyId] IS NOT NULL AND [UnitPriceScale] >= 0 AND [UnitPriceScale] <= 28)";

    /// <summary>
    /// A2a-1 (ALS-D5, ALS-D18): a line is unpriced (all four price columns null) or a GENERAL line with a unit price of zero or more
    /// (an explicit 0 is a price, Sinan #66 6704170096), a
    /// non-empty price unit and at most one discount — a percent in 0..100 or an amount in 0..unit price. That a priced line's
    /// order has a currency spans two tables and is the domain's rule (a CHECK cannot read the header); the price unit equal to
    /// the line's unit is the domain's too (ALS-D51: a published conversion may lift it without a migration).
    /// </summary>
    /// <summary>
    /// A2a-2 (ALS-D38, ALS-D75): a line carries no maturity surcharge, or exactly one — a percent in 0..100 of the discounted price or an
    /// amount of zero or more — beside a unit price ABOVE 0 (Sinan: no surcharge on a 0 price). It is kept apart from
    /// <see cref="OrderLinePriceCheck"/>, which stays as A2a-1 wrote it; a surcharge without a price fails here (UnitPrice NULL).
    /// </summary>
    /// <summary>AK-3 (SAT-D437): both snapshot columns or neither; positive; only on a piece (UnitIsKilogram = 0) general (Kind = 1) line.</summary>
    internal const string OrderLineTheoreticalKilogramsCheck =
        "([TheoreticalKilogramsPerUnit] IS NULL AND [TheoreticalKilogramsSourceVersion] IS NULL) "
        + "OR ([TheoreticalKilogramsPerUnit] IS NOT NULL AND [TheoreticalKilogramsPerUnit] > 0 "
        + "AND [TheoreticalKilogramsSourceVersion] IS NOT NULL AND LEN([TheoreticalKilogramsSourceVersion]) > 0 "
        + "AND [UnitIsKilogram] = 0 AND [Kind] = 1)";

    internal const string OrderLineDeferralCheck =
        "([DeferralPercent] IS NULL AND [DeferralAmount] IS NULL) "
        + "OR ([UnitPrice] IS NOT NULL AND [UnitPrice] > 0 AND ([DeferralPercent] IS NULL OR [DeferralAmount] IS NULL) "
        + "AND ([DeferralPercent] IS NULL OR ([DeferralPercent] >= 0 AND [DeferralPercent] <= 100)) "
        + "AND ([DeferralAmount] IS NULL OR [DeferralAmount] >= 0))";

    internal const string OrderLinePriceCheck =
        "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL AND [DiscountPercent] IS NULL AND [DiscountAmount] IS NULL) "
        + "OR ([Kind] = 1 AND [UnitPrice] IS NOT NULL AND [UnitPrice] >= 0 AND [PriceUnitId] IS NOT NULL AND [PriceUnitId] <> " + EmptyGuid + " "
        + "AND ([DiscountPercent] IS NULL OR [DiscountAmount] IS NULL) "
        + "AND ([DiscountPercent] IS NULL OR ([DiscountPercent] >= 0 AND [DiscountPercent] <= 100)) "
        + "AND ([DiscountAmount] IS NULL OR ([DiscountAmount] >= 0 AND [DiscountAmount] <= [UnitPrice])))";

    /// <summary>ALC-SC33 (FNS-D83): the one rate kind admitted today, the TCMB forex selling rate.</summary>
    internal static readonly string ExchangeRateKindCheck = $"[Kind] IN ({(int)PurchaseOrderExchangeRateKind.TcmbForexSelling})";

    /// <summary>
    /// ALC-SC33 (FNS-D79/D91): an automatic rate names its bulletin day and has no reason; a manual rate names no bulletin day. A CHECK
    /// passes on UNKNOWN, so each column is tested with IS [NOT] NULL.
    /// </summary>
    internal static readonly string ExchangeRateSourceCheck =
        $"([Source] = {(int)PurchaseOrderExchangeRateSource.Automatic} AND [BulletinDate] IS NOT NULL AND [Reason] IS NULL) "
        + $"OR ([Source] = {(int)PurchaseOrderExchangeRateSource.Manual} AND [BulletinDate] IS NULL)";

    /// <summary>One create attempt per stored order; the approve command's attempts share the order id.</summary>
    internal static readonly string OrderCreateAttemptFilter =
        "[OrderId] IS NOT NULL AND [CommandName] = N'" + PurchasingCommands.OrderCreateV1 + "'";

    internal DbSet<PurchaseRequestRow> Requests => Set<PurchaseRequestRow>();

    internal DbSet<PurchaseRequestLineRow> RequestLines => Set<PurchaseRequestLineRow>();

    internal DbSet<PurchaseOrderRow> Orders => Set<PurchaseOrderRow>();

    internal DbSet<PurchaseOrderLineRow> OrderLines => Set<PurchaseOrderLineRow>();

    internal DbSet<PurchaseAcceptanceRow> Acceptances => Set<PurchaseAcceptanceRow>();

    /// <summary>A3 API-30 (ALS-D33): the delivery slices of every recorded schedule revision (append only).</summary>
    internal DbSet<PurchaseOrderLineDeliverySliceRow> DeliverySlices => Set<PurchaseOrderLineDeliverySliceRow>();

    internal DbSet<PurchaseAttemptRow> Attempts => Set<PurchaseAttemptRow>();

    /// <summary>API-11 (F-017 §3.6): every closure and reopening of a general order line (append only).</summary>
    internal DbSet<PurchaseOrderLineClosureEventRow> LineClosureEvents => Set<PurchaseOrderLineClosureEventRow>();

    /// <summary>ALC-SC33 (FNS-D79/D91): every exchange rate recorded on an order — the opening default and each change (append only).</summary>
    internal DbSet<PurchaseOrderExchangeRateChangeRow> ExchangeRateChanges => Set<PurchaseOrderExchangeRateChangeRow>();

    /// <summary>API-11 (ADR-009): Purchasing's transactional outbox (append only).</summary>
    internal DbSet<PurchasingOutboxRow> OutboxMessages => Set<PurchasingOutboxRow>();

    internal DbSet<PurchaseGoodsReceiptBatchRow> GoodsReceiptBatches => Set<PurchaseGoodsReceiptBatchRow>();

    internal DbSet<PurchaseGoodsReceiptBatchLabelRow> GoodsReceiptBatchLabels => Set<PurchaseGoodsReceiptBatchLabelRow>();

    internal DbSet<PurchaseGoodsReceiptBatchAttemptRow> GoodsReceiptBatchAttempts => Set<PurchaseGoodsReceiptBatchAttemptRow>();

    /// <summary>ALC-S5 (B-19; SAT-D306/SAT-D393): returns to supplier of received labels.</summary>
    internal DbSet<PurchaseSupplierReturnRow> SupplierReturns => Set<PurchaseSupplierReturnRow>();

    internal DbSet<PurchaseSupplierReturnAttemptRow> SupplierReturnAttempts => Set<PurchaseSupplierReturnAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureRequests(modelBuilder);
        ConfigureRequestLines(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureOrderLines(modelBuilder);
        ConfigureAcceptances(modelBuilder);
        ConfigureDeliverySlices(modelBuilder);
        ConfigureLineClosureEvents(modelBuilder);
        ConfigureExchangeRateChanges(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigureGoodsReceiptBatches(modelBuilder);
        ConfigureSupplierReturns(modelBuilder);
        ConfigureReceiptAdjustments(modelBuilder);
        ConfigureSupplierRevisions(modelBuilder);
        ConfigureIncomingInvoices(modelBuilder);
        ConfigureInvoiceImports(modelBuilder);
        ConfigureServiceOrders(modelBuilder);
        ConfigureAdditionalCosts(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureRequests(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseRequestRow>();
        entity.ToTable("PurchaseRequests", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequests_Identity", $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseRequests_Status", "[Status] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_PurchaseRequests_Version", "[Version] >= 1 AND [SourcingVersion] >= 0");
            table.HasCheckConstraint("CK_PurchaseRequests_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
            // A1-1: the decision facts match the status (a CHECK passes on UNKNOWN, so every fact is tested with IS [NOT] NULL).
            table.HasCheckConstraint("CK_PurchaseRequests_Decision", RequestDecisionCheck);
            // ALS-D2 (defence in depth beside the domain): a decision never comes from the preparer or the submitter.
            table.HasCheckConstraint("CK_PurchaseRequests_MakerChecker", RequestMakerCheckerCheck);
            // ALS-D46/ALS-D47 (defence in depth beside the domain): only the preparer or the submitter withdraws, with a reason.
            table.HasCheckConstraint("CK_PurchaseRequests_Withdrawal", RequestWithdrawalCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.EffectiveOn).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SubmittedBy).HasMaxLength(128);
        entity.Property(x => x.ApprovedBy).HasMaxLength(128);
        entity.Property(x => x.RejectedBy).HasMaxLength(128);
        entity.Property(x => x.RejectionReason).HasMaxLength(4000);
        entity.Property(x => x.WithdrawnBy).HasMaxLength(128);
        entity.Property(x => x.WithdrawalReason).HasMaxLength(4000);
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_PurchaseRequests_CompanyId_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_PurchaseRequests_CompanyId_Status_CreatedAtUtc_Id");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureRequestLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseRequestLineRow>();
        entity.ToTable("PurchaseRequestLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequestLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_PurchaseRequestLines_Identity",
                $"[LineId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid} AND LEN([ProductVersion]) > 0");
            table.HasCheckConstraint("CK_PurchaseRequestLines_Quantity", "[Quantity] > 0");
            // A1-2 (plan API-02): a source is a defined kind with both opaque identities, or (manual) none of the three.
            table.HasCheckConstraint("CK_PurchaseRequestLines_Source", RequestLineSourceCheck);
        });
        entity.HasKey(x => new { x.CompanyId, x.RequestId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.RequestId, x.LineId }).IsUnique().HasDatabaseName("UX_PurchaseRequestLines_LineId");
        // A1-2: the read the Sales adaptor (S-A1/MOD-03) and the Üretim action (A-2/U5-c) need — the lines of one source record.
        entity.HasIndex(x => new { x.CompanyId, x.SourceKind, x.SourceDocumentId }).HasDatabaseName("IX_PurchaseRequestLines_Source")
            .HasFilter("[SourceKind] IS NOT NULL");
        entity.Property(x => x.ProductVersion).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.HasOne<PurchaseRequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOrders(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseOrderRow>();
        entity.ToTable("PurchaseOrders", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrders_Identity",
                $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [SupplierPartyId] <> {EmptyGuid} AND [CompanyAccountId] <> {EmptyGuid} "
                + $"AND ([SourceRequestId] IS NULL OR [SourceRequestId] <> {EmptyGuid})");
            table.HasCheckConstraint("CK_PurchaseOrders_Status", "[Status] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_PurchaseOrders_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_PurchaseOrders_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
            // A1-3 (No.161 §2): the cancellation facts match the status.
            table.HasCheckConstraint("CK_PurchaseOrders_Cancellation", OrderCancellationCheck);
            // A2a-5a (ALS-D3, defence in depth beside the domain): the approver is never the preparer.
            table.HasCheckConstraint("CK_PurchaseOrders_Approval", OrderApprovalCheck);
            // A2a-1 (TEK-01): the money snapshot is complete or absent.
            table.HasCheckConstraint("CK_PurchaseOrders_Money", OrderMoneyCheck);
            table.HasCheckConstraint("CK_PurchaseOrders_DocumentNumber", OrderDocumentNumberCheck);
            // A2a-1c (ALS-D72): the unit-price scale only beside a money snapshot.
            table.HasCheckConstraint("CK_PurchaseOrders_UnitPriceScale", OrderUnitPriceScaleCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.EffectiveOn).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CancelledBy).HasMaxLength(128);
        entity.Property(x => x.CancellationReason).HasMaxLength(4000);
        entity.Property(x => x.ApprovedBy).HasMaxLength(128);
        entity.Property(x => x.CurrencyCode).HasMaxLength(PurchaseOrderMoney.MaximumCurrencyCodeLength);
        entity.Property(x => x.DocumentNumber).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique().HasFilter("[DocumentNumber] IS NOT NULL");
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_PurchaseOrders_CompanyId_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_PurchaseOrders_CompanyId_Status_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.SourceRequestId }).HasDatabaseName("IX_PurchaseOrders_CompanyId_SourceRequestId");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseRequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceRequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOrderLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseOrderLineRow>();
        entity.ToTable("PurchaseOrderLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_PurchaseOrderLines_Identity",
                $"[LineId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid} AND LEN([ProductVersion]) > 0 "
                + $"AND ([ParentLineId] IS NULL OR [ParentLineId] <> {EmptyGuid}) AND ([SourceRequestLineId] IS NULL OR [SourceRequestLineId] <> {EmptyGuid})");
            // A general line has no parent; a technical line is a slice of one general line (§7.2).
            table.HasCheckConstraint("CK_PurchaseOrderLines_Kind",
                "([Kind] = 1 AND [ParentLineId] IS NULL) OR ([Kind] = 2 AND [ParentLineId] IS NOT NULL)");
            // §7.2 / §13: technical slices, the accepted share and (A1-3) the cancelled remainder never exceed the first commitment.
            table.HasCheckConstraint("CK_PurchaseOrderLines_Shares", OrderLineSharesCheck);
            // API-11: a remainder is cancelled with the order or closed on the line, never both.
            table.HasCheckConstraint("CK_PurchaseOrderLines_ClosedOrCancelled", OrderLineClosedOrCancelledCheck);
            // A2a-1 (ALS-D5, ALS-D18): unpriced, or a general line with a price ≥ 0 and at most one bounded discount.
            table.HasCheckConstraint("CK_PurchaseOrderLines_Price", OrderLinePriceCheck);
            // A2a-2 (ALS-D38, ALS-D75): at most one maturity surcharge, only beside a unit price above 0.
            table.HasCheckConstraint("CK_PurchaseOrderLines_Deferral", OrderLineDeferralCheck);
            // AK-3 (SAT-D437): the theoretical kg/piece snapshot — both columns or neither, positive, only on a piece general line.
            table.HasCheckConstraint("CK_PurchaseOrderLines_TheoreticalKilograms", OrderLineTheoreticalKilogramsCheck);
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId, x.LineNo });
        entity.HasAlternateKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .HasName("AK_PurchaseOrderLines_CompanyId_OrderId_LineId");
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.LineId }).IsUnique().HasDatabaseName("UX_PurchaseOrderLines_LineId");
        entity.HasIndex(x => new { x.CompanyId, x.SourceRequestLineId }).HasDatabaseName("IX_PurchaseOrderLines_SourceRequestLine");
        entity.Property(x => x.ProductVersion).HasMaxLength(64).IsRequired();
        foreach (var quantity in new[]
                 {
                     nameof(PurchaseOrderLineRow.Ordered), nameof(PurchaseOrderLineRow.Technical), nameof(PurchaseOrderLineRow.Accepted),
                     nameof(PurchaseOrderLineRow.Cancelled), nameof(PurchaseOrderLineRow.Closed), nameof(PurchaseOrderLineRow.DefinitiveReturned),
                 })
        {
            entity.Property<decimal>(quantity).HasPrecision(18, 3);
        }

        // A2a-1: decimal(24,6) holds 18 integer digits and PurchaseOrderMoney.MaximumPriceScale decimals; the domain refuses wider values.
        entity.Property(x => x.UnitPrice).HasPrecision(24, PurchaseOrderMoney.MaximumPriceScale);
        entity.Property(x => x.DiscountAmount).HasPrecision(24, PurchaseOrderMoney.MaximumPriceScale);
        entity.Property(x => x.DiscountPercent).HasPrecision(9, PurchaseOrderLinePrice.PercentScale);
        // A2a-2: the maturity surcharge in the same precisions as the discount (a percent decimal(9,6), an amount decimal(24,6)).
        entity.Property(x => x.DeferralAmount).HasPrecision(24, PurchaseOrderMoney.MaximumPriceScale);
        entity.Property(x => x.DeferralPercent).HasPrecision(9, PurchaseOrderLinePrice.PercentScale);
        entity.Property(x => x.TheoreticalKilogramsPerUnit).HasPrecision(18, PurchaseOrderLineTheoreticalKilograms.KilogramsPerUnitScale);
        entity.Property(x => x.TheoreticalKilogramsSourceVersion).HasMaxLength(PurchaseOrderLineTheoreticalKilograms.SourceVersionMaxLength);

        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseAttemptRow>();
        entity.ToTable("PurchaseAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_PurchaseAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_PurchaseAttempts_ReceiptAdjustment",
                $"[ReceiptAdjustmentId] IS NULL OR ([ReceiptAdjustmentId] <> {EmptyGuid} AND [Outcome] = 1 AND [OrderId] IS NOT NULL AND [RequestId] IS NULL)");
            // ADR-011 §7: a stored result (exactly one of request / order) or a recorded terminal refusal, never both.
            table.HasCheckConstraint("CK_PurchaseAttempts_Outcome",
                $"([Outcome] = 1 AND [RefusalJson] IS NULL AND (([RequestId] IS NOT NULL AND [RequestId] <> {EmptyGuid} AND [OrderId] IS NULL) "
                + $"OR ([OrderId] IS NOT NULL AND [OrderId] <> {EmptyGuid} AND [RequestId] IS NULL))) "
                + "OR ([Outcome] = 2 AND [RequestId] IS NULL AND [OrderId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        entity.HasIndex(x => new { x.CompanyId, x.RequestId }).IsUnique().HasDatabaseName("UX_PurchaseAttempts_RequestId")
            .HasFilter(RequestCreateAttemptFilter);
        entity.HasIndex(x => new { x.CompanyId, x.OrderId }).IsUnique().HasDatabaseName("UX_PurchaseAttempts_OrderId")
            .HasFilter(OrderCreateAttemptFilter);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseRequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAcceptances(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseAcceptanceRow>();
        entity.ToTable("PurchaseAcceptanceShares", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Identity",
                $"[AcceptanceId] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Quantity", "[Quantity] > 0");
            // AK-3 (ALS-D119): a piece-line acceptance's weighed kilograms, positive when present.
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_MeasuredKg", "[MeasuredQuantityKg] IS NULL OR [MeasuredQuantityKg] > 0");
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Version", "[OrderVersionAfter] >= 1");
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Text",
                "LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
        });
        entity.HasKey(x => x.AcceptanceId);
        entity.HasAlternateKey(x => new { x.CompanyId, x.AcceptanceId });
        entity.HasAlternateKey(x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId });
        entity.Property(x => x.AcceptanceId).ValueGeneratedNever();
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.MeasuredQuantityKg).HasPrecision(18, 3);
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.IdempotencyKeyHash }).IsUnique()
            .HasDatabaseName("UX_PurchaseAcceptanceShares_Idempotency");
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.LineId, x.RecordedAtUtc, x.AcceptanceId })
            .HasDatabaseName("IX_PurchaseAcceptanceShares_OrderLineRecorded");
        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// A3 API-30 (ALS-D33, ALS-D21): a schedule revision is the set of rows of one (company, order, version); its slices are numbered
    /// 1..n (SliceNo, "Sıra No") and named by a distinct id, each with a quantity above zero in the line's unit and a delivery date.
    /// Bound to the order and to the line itself (the line's alternate key), never to another company's. The Σ = ordered rule is the
    /// domain's, measured again on every rebuild.
    /// </summary>
    private void ConfigureDeliverySlices(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseOrderLineDeliverySliceRow>();
        entity.ToTable("PurchaseOrderLineDeliverySlices", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderLineDeliverySlices_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid} AND [SliceId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseOrderLineDeliverySlices_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_PurchaseOrderLineDeliverySlices_SliceNo",
                "[SliceNo] >= 1 AND [SliceNo] <= " + PurchaseOrder.MaximumScheduleSlices.ToString(System.Globalization.CultureInfo.InvariantCulture));
            table.HasCheckConstraint("CK_PurchaseOrderLineDeliverySlices_Version", "[OrderVersionAfter] >= 2");
            table.HasCheckConstraint("CK_PurchaseOrderLineDeliverySlices_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter, x.SliceNo });
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter, x.SliceId }).IsUnique()
            .HasDatabaseName("UX_PurchaseOrderLineDeliverySlices_SliceId");
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.LineId, x.OrderVersionAfter })
            .HasDatabaseName("IX_PurchaseOrderLineDeliverySlices_OrderLineVersion");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.DeliveryDate).HasColumnType("date");
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// API-11 (ALS-D10/ALS-D12, F-017 §3.6): one row per closure or reopening, keyed by the order version it produced (one command,
    /// one row); bound to the order and to the line itself (the line's alternate key), never to another company's. Each row has its
    /// actor, time and non-blank reason. That the line is a general line and the events alternate (close, reopen, close…) is the
    /// domain's rule, measured again on every rebuild.
    /// </summary>
    private void ConfigureLineClosureEvents(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseOrderLineClosureEventRow>();
        entity.ToTable("PurchaseOrderLineClosureEvents", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderLineClosureEvents_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseOrderLineClosureEvents_Version", "[OrderVersionAfter] >= 3");
            table.HasCheckConstraint("CK_PurchaseOrderLineClosureEvents_Text",
                "LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter });
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.LineId, x.OrderVersionAfter })
            .HasDatabaseName("IX_PurchaseOrderLineClosureEvents_OrderLineVersion");
        entity.Property(x => x.Reason).HasMaxLength(PurchaseOrder.MaximumReasonLength).IsRequired();
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// ALC-SC33 (FNS-D79/D80/D83/D91): one row per recorded order rate, keyed by the order version it produced (the opening default at
    /// version 1, each change at its own version); bound to the order, never to another company's. The rate is positive with the
    /// Settings bounds (decimal(28,10)), the target currency non-empty, the kind and source known; an automatic rate names its bulletin
    /// day and carries no reason, a manual one names no bulletin day. That only a Draft order's rate changes and that the target
    /// differs from the order's currency is the domain's rule, measured again on every rebuild.
    /// </summary>
    private void ConfigureExchangeRateChanges(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseOrderExchangeRateChangeRow>();
        entity.ToTable("PurchaseOrderExchangeRateChanges", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderExchangeRateChanges_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [TargetCurrencyId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseOrderExchangeRateChanges_Version", "[OrderVersionAfter] >= 1");
            table.HasCheckConstraint("CK_PurchaseOrderExchangeRateChanges_Rate", "[Rate] > 0");
            table.HasCheckConstraint("CK_PurchaseOrderExchangeRateChanges_Kind", ExchangeRateKindCheck);
            table.HasCheckConstraint("CK_PurchaseOrderExchangeRateChanges_Source", ExchangeRateSourceCheck);
            table.HasCheckConstraint("CK_PurchaseOrderExchangeRateChanges_Text",
                "([Reason] IS NULL OR LEN([Reason]) > 0) AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter });
        entity.Property(x => x.Rate).HasPrecision(28, PurchaseOrderExchangeRate.MaximumScale);
        entity.Property(x => x.RateDay).HasColumnType("date");
        entity.Property(x => x.BulletinDate).HasColumnType("date");
        entity.Property(x => x.Reason).HasMaxLength(PurchaseOrder.MaximumReasonLength);
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// API-11 (ADR-009; the Sales <c>sales.OutboxMessages</c> shape): an envelope with non-empty identities, a known event type, a
    /// schema version and sequence from 1 and a JSON payload; (company, type, aggregate, sequence) is unique. Append only (the write
    /// guard below).
    /// </summary>
    private void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchasingOutboxRow>();
        entity.Property(x => x.Position).IsRowVersion().HasConversion<byte[]>();
        entity.HasIndex(x => x.Position).HasDatabaseName("IX_OutboxMessages_Position");
        entity.ToTable("OutboxMessages", Schema, table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_Identity",
                $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [AggregateId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_OutboxMessages_Envelope",
                "LEN([EventType]) > 0 AND [SchemaVersion] >= 1 AND LEN([SourceOperation]) > 0 "
                + "AND LEN([AggregateType]) > 0 AND [AggregateVersion] >= 1 AND [Sequence] >= 1 "
                + "AND LEN([CorrelationId]) > 0 AND ISJSON([PayloadJson]) = 1");
            table.HasCheckConstraint("CK_OutboxMessages_EventType", OutboxEventTypeCheck);
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.EventType).HasMaxLength(160).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceOperation).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.AggregateType).HasMaxLength(160).IsUnicode(false).IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.CausationId).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.HasIndex(x => new { x.CompanyId, x.EventType, x.AggregateId, x.Sequence })
            .IsUnique().HasDatabaseName("UX_OutboxMessages_AggregateSequence");
        entity.HasIndex(x => new { x.CompanyId, x.RecordedAtUtc, x.Id }).HasDatabaseName("IX_OutboxMessages_CompanyId_RecordedAtUtc_Id");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// ALC-S16 (KLN-D45): PendingApproval (1) has no decision fact; Posted (2) has none (posted at submission, inside the tolerance) or
    /// a decider and time but no reason; Rejected (3) has a decider, the time and a non-blank reason. A decider is never the preparer
    /// (binary comparison, the domain's ordinal rule). A CHECK passes on UNKNOWN, so every fact is tested with IS [NOT] NULL.
    /// </summary>
    internal const string GoodsReceiptBatchDecisionCheck =
        "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) "
        + "OR ([Status] = 2 AND [DecisionReason] IS NULL AND (([DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL) "
        + "OR ([DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecidedBy] COLLATE " + IdentityCollation + " <> [PreparedBy] COLLATE " + IdentityCollation + "))) "
        + "OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0 "
        + "AND [DecidedBy] COLLATE " + IdentityCollation + " <> [PreparedBy] COLLATE " + IdentityCollation + ")";

    /// <summary>
    /// ALC-S16 (KLN-D45): a batch posted without an approver was exact (1) or inside the tolerance (2) and acknowledged; outside (3) or
    /// with no tolerance set (4) it never posts without a decider. The tolerance snapshot is 0..100 when present and absent for (4).
    /// AK-3b (BS-16/BS-17): a piece batch ([KilogramsPerUnit] set) compares theoretical with weighed kg as a warning only, so it is
    /// always Posted without a decider, whatever the outcome, and needs no acknowledgement.
    /// </summary>
    internal const string GoodsReceiptBatchVarianceCheck =
        "[VarianceOutcome] IN (1, 2, 3, 4) AND [ExpectedQuantityKg] > 0 AND [ReceivedQuantityKg] > 0 "
        + "AND ([TolerancePercent] IS NULL OR ([TolerancePercent] >= 0 AND [TolerancePercent] <= 100)) "
        + "AND (([VarianceOutcome] = 4 AND [TolerancePercent] IS NULL) OR ([VarianceOutcome] IN (2, 3) AND [TolerancePercent] IS NOT NULL) OR [VarianceOutcome] = 1) "
        + "AND (([KilogramsPerUnit] IS NULL AND ([VarianceOutcome] <> 2 OR [VarianceAcknowledged] = 1) "
        + "AND ([Status] <> 2 OR [VarianceOutcome] IN (1, 2) OR [DecidedBy] IS NOT NULL)) "
        + "OR ([KilogramsPerUnit] IS NOT NULL AND [KilogramsPerUnit] > 0 AND [Status] = 2 AND [DecidedBy] IS NULL))";

    /// <summary>The variance rule before AK-3b; the AddPurchaseGoodsReceiptBatchPieceFacts rollback restores it.</summary>
    internal const string GoodsReceiptBatchVarianceCheckBeforePieceFacts =
        "[VarianceOutcome] IN (1, 2, 3, 4) AND [ExpectedQuantityKg] > 0 AND [ReceivedQuantityKg] > 0 "
        + "AND ([TolerancePercent] IS NULL OR ([TolerancePercent] >= 0 AND [TolerancePercent] <= 100)) "
        + "AND (([VarianceOutcome] = 4 AND [TolerancePercent] IS NULL) OR ([VarianceOutcome] IN (2, 3) AND [TolerancePercent] IS NOT NULL) OR [VarianceOutcome] = 1) "
        + "AND ([VarianceOutcome] <> 2 OR [VarianceAcknowledged] = 1) "
        + "AND ([Status] <> 2 OR [VarianceOutcome] IN (1, 2) OR [DecidedBy] IS NOT NULL)";

    /// <summary>The unreleased-label filter of the company-wide label code uniqueness (KLN-D41).</summary>
    internal const string GoodsReceiptBatchLabelHeldFilter = "[Released] = 0";

    private void ConfigureGoodsReceiptBatches(ModelBuilder modelBuilder)
    {
        var batch = modelBuilder.Entity<PurchaseGoodsReceiptBatchRow>();
        batch.ToTable("PurchaseGoodsReceiptBatches", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatches_Identity",
                $"[BatchId] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid} "
                + $"AND [ProductId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid} AND [LocationId] <> {EmptyGuid} AND LEN([ProductVersion]) > 0");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatches_Status", "[Status] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatches_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatches_Text", "LEN([PreparedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatches_Decision", GoodsReceiptBatchDecisionCheck);
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatches_Variance", GoodsReceiptBatchVarianceCheck);
        });
        batch.HasKey(x => x.BatchId);
        batch.HasAlternateKey(x => new { x.CompanyId, x.BatchId });
        batch.Property(x => x.BatchId).ValueGeneratedNever();
        batch.Property(x => x.Version).IsConcurrencyToken();
        batch.Property(x => x.RowVersion).IsRowVersion();
        batch.Property(x => x.ProductVersion).HasMaxLength(64).IsRequired();
        batch.Property(x => x.ExpectedQuantityKg).HasPrecision(18, 3);
        batch.Property(x => x.ReceivedQuantityKg).HasPrecision(18, 3);
        batch.Property(x => x.TolerancePercent).HasPrecision(9, 6);
        batch.Property(x => x.KilogramsPerUnit).HasPrecision(18, PurchaseOrderLineTheoreticalKilograms.KilogramsPerUnitScale);
        batch.Property(x => x.PreparedBy).HasMaxLength(128).IsRequired();
        batch.Property(x => x.DecidedBy).HasMaxLength(128);
        batch.Property(x => x.DecisionReason).HasMaxLength(PurchaseGoodsReceiptBatchMaximumReasonLength);
        batch.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        batch.HasIndex(x => new { x.CompanyId, x.OrderId, x.PreparedAtUtc, x.BatchId }).HasDatabaseName("IX_PurchaseGoodsReceiptBatches_Order");
        batch.HasIndex(x => new { x.CompanyId, x.Status, x.PreparedAtUtc, x.BatchId }).HasDatabaseName("IX_PurchaseGoodsReceiptBatches_Status");
        batch.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        batch.HasOne<PurchaseOrderLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        batch.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var label = modelBuilder.Entity<PurchaseGoodsReceiptBatchLabelRow>();
        label.ToTable("PurchaseGoodsReceiptBatchLabels", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [BatchId] <> {EmptyGuid} AND [AcceptanceId] <> {EmptyGuid} AND [LotId] <> {EmptyGuid} "
                + $"AND [LabelId] <> {EmptyGuid} AND [MovementId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_Quantity", "[MeasuredQuantityKg] > 0");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_PieceCount", "[PieceCount] IS NULL OR [PieceCount] > 0");
            // KLN-D41: an entered (2) or supplier (1) code, or a generated (3) one with its number; a supplier code equals the supplier number.
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_Code",
                "LEN([LabelCode]) > 0 AND (([CodeSource] = 3 AND [GeneratedNumber] IS NOT NULL AND [GeneratedNumber] >= 1) "
                + "OR ([CodeSource] = 2 AND [GeneratedNumber] IS NULL) "
                + "OR ([CodeSource] = 1 AND [GeneratedNumber] IS NULL AND [SupplierLabelNumber] IS NOT NULL "
                + "AND [SupplierLabelNumber] COLLATE " + IdentityCollation + " = [LabelCode] COLLATE " + IdentityCollation + "))");
        });
        label.HasKey(x => new { x.CompanyId, x.BatchId, x.LineNo });
        label.Property(x => x.LabelCode).HasMaxLength(64).IsRequired();
        label.Property(x => x.SupplierLabelNumber).HasMaxLength(64);
        label.Property(x => x.HeatNumber).HasMaxLength(64);
        label.Property(x => x.CoilNumber).HasMaxLength(64);
        label.Property(x => x.MeasuredQuantityKg).HasPrecision(18, 3);
        // KLN-D41: our code is unique in the company among the labels a batch still holds (case-insensitive, the default collation).
        label.HasIndex(x => new { x.CompanyId, x.LabelCode }).IsUnique().HasFilter(GoodsReceiptBatchLabelHeldFilter)
            .HasDatabaseName("UX_PurchaseGoodsReceiptBatchLabels_LabelCode");
        label.HasIndex(x => new { x.CompanyId, x.SupplierLabelNumber }).HasFilter("[SupplierLabelNumber] IS NOT NULL")
            .HasDatabaseName("IX_PurchaseGoodsReceiptBatchLabels_SupplierLabelNumber");
        label.HasIndex(x => new { x.CompanyId, x.GeneratedNumber }).HasFilter("[GeneratedNumber] IS NOT NULL")
            .HasDatabaseName("IX_PurchaseGoodsReceiptBatchLabels_GeneratedNumber");
        label.HasIndex(x => new { x.CompanyId, x.AcceptanceId }).IsUnique().HasDatabaseName("UX_PurchaseGoodsReceiptBatchLabels_AcceptanceId");
        label.HasIndex(x => new { x.CompanyId, x.LabelId }).IsUnique().HasDatabaseName("UX_PurchaseGoodsReceiptBatchLabels_LabelId");
        label.HasOne<PurchaseGoodsReceiptBatchRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BatchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.BatchId })
            .OnDelete(DeleteBehavior.Restrict);
        label.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<PurchaseGoodsReceiptBatchAttemptRow>();
        attempt.ToTable("PurchaseGoodsReceiptBatchAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_PurchaseGoodsReceiptBatchAttempts_Outcome",
                $"([Outcome] = 1 AND [RefusalJson] IS NULL AND [BatchId] IS NOT NULL AND [BatchId] <> {EmptyGuid}) "
                + "OR ([Outcome] = 2 AND [BatchId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.HasIndex(x => new { x.CompanyId, x.BatchId }).HasDatabaseName("IX_PurchaseGoodsReceiptBatchAttempts_BatchId");
        attempt.HasOne<PurchaseGoodsReceiptBatchRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BatchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.BatchId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private const int PurchaseGoodsReceiptBatchMaximumReasonLength = Domain.GoodsReceipts.PurchaseGoodsReceiptBatch.MaximumReasonLength;

    /// <summary>
    /// ALC-S5 (SAT-D392/SAT-D393): a return recorded at the invoice unit cost (basis 1) is Recorded (2) at once and never decided; a
    /// manual cost (basis 2) waits as PendingCostException (1) with no decision fact, then is Recorded (2) by an approver (no reason)
    /// or Rejected (3) with a reason. Whether the decider may be the requester is a configurable policy (SAT-D396, NEEDS-SINAN), so the
    /// table does not lock it. A CHECK passes on UNKNOWN, so every fact is tested with IS [NOT] NULL.
    /// </summary>
    internal const string SupplierReturnDecisionCheck =
        "([CostBasis] = 1 AND [Status] = 2 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) "
        + "OR ([CostBasis] = 2 AND [Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) "
        + "OR ([CostBasis] = 2 AND [Status] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NULL) "
        + "OR ([CostBasis] = 2 AND [Status] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0)";

    /// <summary>
    /// AK-8 (SAT-D433/SAT-D441/SAT-D445): a return either leaves the receipt's own label (both facts null) or names the foreign
    /// source label together with its mode, lineage (1) or quantity (2).
    /// </summary>
    // AK-8b: per-receipt parts and a piece count exist only on a foreign-source-label return; the parts are stored JSON.
    internal const string SupplierReturnLineagePartsCheck =
        "([LineagePartsJson] IS NULL OR ([SourceLabelId] IS NOT NULL AND ISJSON([LineagePartsJson]) = 1)) "
        + "AND ([PieceCount] IS NULL OR ([SourceLabelId] IS NOT NULL AND [PieceCount] >= 1))";

    internal const string SupplierReturnLineageCheck =
        "([SourceLabelId] IS NULL AND [LineageMode] IS NULL) "
        + "OR ([SourceLabelId] IS NOT NULL AND [SourceLabelId] <> " + EmptyGuid + " AND [LineageMode] IN (1, 2))";

    private void ConfigureSupplierReturns(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PurchaseSupplierReturnRow>();
        entity.ToTable("PurchaseSupplierReturns", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Identity",
                $"[ReturnId] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [ReturnLineId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} "
                + $"AND [LineId] <> {EmptyGuid} AND [GoodsAcceptanceId] <> {EmptyGuid} AND [MovementId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Status", "[Status] IN (1, 2, 3) AND [CostBasis] IN (1, 2)");
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Values",
                "[QuantityKg] > 0 AND [UnitCostTryPerKg] > 0 AND [SourceBlock] IN (N'Free', N'Blocked') AND [ReplacementExpected] = 0");
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Text",
                "LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Decision", SupplierReturnDecisionCheck);
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_CostAllocationJson",
                "[CostAllocationJson] IS NULL OR ISJSON([CostAllocationJson]) = 1");
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_Lineage", SupplierReturnLineageCheck);
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_LineageParts", SupplierReturnLineagePartsCheck);
        });
        entity.HasKey(x => x.ReturnId);
        entity.HasAlternateKey(x => new { x.CompanyId, x.ReturnId });
        entity.Property(x => x.ReturnId).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.SourceBlock).HasMaxLength(16).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.QuantityKg).HasPrecision(18, 3);
        entity.Property(x => x.UnitCostTryPerKg).HasPrecision(18, 4);
        entity.Property(x => x.CostAllocationJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.LineagePartsJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.Reason).HasMaxLength(PurchaseSupplierReturnMaximumReasonLength).IsRequired();
        entity.Property(x => x.RequestedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.DecidedBy).HasMaxLength(128);
        entity.Property(x => x.DecisionReason).HasMaxLength(PurchaseSupplierReturnMaximumReasonLength);
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.RequestedAtUtc, x.ReturnId }).HasDatabaseName("IX_PurchaseSupplierReturns_Order");
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.RequestedAtUtc, x.ReturnId }).HasDatabaseName("IX_PurchaseSupplierReturns_Status");
        // One stock exit is one return (the movement id is the exit's idempotent identity in Inventory).
        entity.HasIndex(x => new { x.CompanyId, x.MovementId }).IsUnique().HasDatabaseName("UX_PurchaseSupplierReturns_MovementId");
        entity.HasIndex(x => new { x.CompanyId, x.GoodsAcceptanceId }).HasDatabaseName("IX_PurchaseSupplierReturns_GoodsAcceptanceId");
        entity.HasOne<PurchaseOrderRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PurchaseOrderLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<PurchaseSupplierReturnAttemptRow>();
        attempt.ToTable("PurchaseSupplierReturnAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseSupplierReturnAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_PurchaseSupplierReturnAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_PurchaseSupplierReturnAttempts_Outcome",
                $"([Outcome] = 1 AND [RefusalJson] IS NULL AND [ReturnId] IS NOT NULL AND [ReturnId] <> {EmptyGuid}) "
                + "OR ([Outcome] = 2 AND [ReturnId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.HasIndex(x => new { x.CompanyId, x.ReturnId }).HasDatabaseName("IX_PurchaseSupplierReturnAttempts_ReturnId");
        attempt.HasOne<PurchaseSupplierReturnRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReturnId })
            .HasPrincipalKey(x => new { x.CompanyId, x.ReturnId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private const int PurchaseSupplierReturnMaximumReasonLength = Domain.Returns.PurchaseSupplierReturn.MaximumReasonLength;

    private void ApplyWriteGuards()
    {
        PurchasingAuditEventTenantGuard.Validate(this, companyContext);
        GuardSupplierRevisionHistory();
        GuardIncomingInvoiceSnapshots();
        GuardServiceOrders();
        GuardAdditionalCosts();
        foreach (var entry in ChangeTracker.Entries<IPurchasingTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Satınalma kayıtları silinmez; değişiklik yeni bir kayıttır.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Satınalma kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Satınalma kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is PurchaseOrderRow && entry.Property(nameof(PurchaseOrderRow.DocumentNumberCancelledAtUtc)).IsModified
                    && (entry.Property(nameof(PurchaseOrderRow.DocumentNumberCancelledAtUtc)).OriginalValue is not null
                        || entry.Property(nameof(PurchaseOrderRow.DocumentNumberCancelledAtUtc)).CurrentValue is null))
                    throw new InvalidOperationException("Belge numarası iptal geçmişi yalnız bir kez eklenir.");
                // AK-3 (SAT-D437): the theoretical kg per piece is recorded once at the first approval and never changes after.
                if (entry.Entity is PurchaseOrderLineRow
                    && (entry.Property(nameof(PurchaseOrderLineRow.TheoreticalKilogramsPerUnit)).IsModified
                        || entry.Property(nameof(PurchaseOrderLineRow.TheoreticalKilogramsSourceVersion)).IsModified)
                    && (entry.Property(nameof(PurchaseOrderLineRow.TheoreticalKilogramsPerUnit)).OriginalValue is not null
                        || entry.Property(nameof(PurchaseOrderLineRow.TheoreticalKilogramsSourceVersion)).OriginalValue is not null))
                    throw new InvalidOperationException("Teorik kg/adet onayda bir kez kaydedilir, sonra değişmez.");
                string[] allowed = entry.Entity switch
                {
                    PurchaseRequestRow =>
                    [
                        nameof(PurchaseRequestRow.Status), nameof(PurchaseRequestRow.UpdatedAtUtc), nameof(PurchaseRequestRow.SourcingVersion),
                        nameof(PurchaseRequestRow.SubmittedBy), nameof(PurchaseRequestRow.ApprovedBy), nameof(PurchaseRequestRow.ApprovedAtUtc),
                        nameof(PurchaseRequestRow.RejectedBy), nameof(PurchaseRequestRow.RejectedAtUtc), nameof(PurchaseRequestRow.RejectionReason),
                        nameof(PurchaseRequestRow.WithdrawnBy), nameof(PurchaseRequestRow.WithdrawnAtUtc), nameof(PurchaseRequestRow.WithdrawalReason),
                    ],
                    PurchaseOrderRow =>
                    [
                        nameof(PurchaseOrderRow.Status), nameof(PurchaseOrderRow.UpdatedAtUtc),
                        nameof(PurchaseOrderRow.ApprovedBy), nameof(PurchaseOrderRow.ApprovedAtUtc),
                        nameof(PurchaseOrderRow.CancelledBy), nameof(PurchaseOrderRow.CancelledAtUtc), nameof(PurchaseOrderRow.CancellationReason),
                        nameof(PurchaseOrderRow.DocumentNumberCancelledAtUtc),
                        nameof(PurchaseOrderRow.SupplierRevisionHistoryJson), nameof(PurchaseOrderRow.CurrentPreparedBy),
                        nameof(PurchaseOrderRow.SupplierPartyId), nameof(PurchaseOrderRow.CompanyAccountId),
                    ],
                    PurchaseOrderLineRow =>
                    [
                        nameof(PurchaseOrderLineRow.Technical), nameof(PurchaseOrderLineRow.Accepted), nameof(PurchaseOrderLineRow.Cancelled),
                        nameof(PurchaseOrderLineRow.Closed), nameof(PurchaseOrderLineRow.DefinitiveReturned),
                        nameof(PurchaseOrderLineRow.ReceiptClosureRevived),
                        nameof(PurchaseOrderLineRow.UnitPrice), nameof(PurchaseOrderLineRow.PriceUnitId),
                        nameof(PurchaseOrderLineRow.DiscountPercent), nameof(PurchaseOrderLineRow.DiscountAmount),
                        nameof(PurchaseOrderLineRow.DeferralPercent), nameof(PurchaseOrderLineRow.DeferralAmount),
                        nameof(PurchaseOrderLineRow.TheoreticalKilogramsPerUnit), nameof(PurchaseOrderLineRow.TheoreticalKilogramsSourceVersion),
                    ],
                    // ALC-S16: a bulk receipt moves once (its status and decision facts); a label is only released by a rejection.
                    PurchaseGoodsReceiptBatchRow =>
                    [
                        nameof(PurchaseGoodsReceiptBatchRow.Status), nameof(PurchaseGoodsReceiptBatchRow.DecidedBy),
                        nameof(PurchaseGoodsReceiptBatchRow.DecidedAtUtc), nameof(PurchaseGoodsReceiptBatchRow.DecisionReason),
                    ],
                    PurchaseGoodsReceiptBatchLabelRow => [nameof(PurchaseGoodsReceiptBatchLabelRow.Released)],
                    PurchaseServiceOrderRow => [nameof(PurchaseServiceOrderRow.Version)],
                    // ALC-S5: a supplier return moves once (a pending cost exception is approved or rejected).
                    PurchaseSupplierReturnRow =>
                    [
                        nameof(PurchaseSupplierReturnRow.Status), nameof(PurchaseSupplierReturnRow.DecidedBy),
                        nameof(PurchaseSupplierReturnRow.DecidedAtUtc), nameof(PurchaseSupplierReturnRow.DecisionReason),
                    ],
                    _ => [],
                };
                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException("Satınalma kaydının yalnız izinli alanı değişir; satırlar ve deneme kayıtları eklenir, değişmez.");
                }
            }
        }
    }
}
