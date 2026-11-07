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
/// version / time, a request's sourcing version; an order line's technical, accepted and (A1-3) cancelled totals; attempts
/// append only). The attempt key (company, command, key hash) is a primary key under a binary collation; the raw key
/// is never stored. The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// Nothing here is physical: an approved order is expected supply, never stock.
/// </summary>
public sealed class PurchasingDbContext(DbContextOptions<PurchasingDbContext> options, ICompanyContext companyContext)
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
        + "OR ([Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL)";

    /// <summary>
    /// §7.2 / §13 and A1-3 (master INV-006): technical slices, the accepted share and the cancelled remainder are never negative
    /// and together never exceed the first commitment.
    /// </summary>
    internal const string OrderLineSharesCheck =
        "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Cancelled] >= 0 AND [Technical] + [Accepted] + [Cancelled] <= [Ordered]";

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
    /// A2a-1 (ALS-D5, ALS-D18): a line is unpriced (all four price columns null) or a GENERAL line with a positive unit price, a
    /// non-empty price unit and at most one discount — a percent in 0..100 or an amount in 0..unit price. That a priced line's
    /// order has a currency spans two tables and is the domain's rule (a CHECK cannot read the header); the price unit equal to
    /// the line's unit is the domain's too (ALS-D51: a published conversion may lift it without a migration).
    /// </summary>
    internal const string OrderLinePriceCheck =
        "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL AND [DiscountPercent] IS NULL AND [DiscountAmount] IS NULL) "
        + "OR ([Kind] = 1 AND [UnitPrice] IS NOT NULL AND [UnitPrice] > 0 AND [PriceUnitId] IS NOT NULL AND [PriceUnitId] <> " + EmptyGuid + " "
        + "AND ([DiscountPercent] IS NULL OR [DiscountAmount] IS NULL) "
        + "AND ([DiscountPercent] IS NULL OR ([DiscountPercent] >= 0 AND [DiscountPercent] <= 100)) "
        + "AND ([DiscountAmount] IS NULL OR ([DiscountAmount] >= 0 AND [DiscountAmount] <= [UnitPrice])))";

    /// <summary>One create attempt per stored order; the approve command's attempts share the order id.</summary>
    internal static readonly string OrderCreateAttemptFilter =
        "[OrderId] IS NOT NULL AND [CommandName] = N'" + PurchasingCommands.OrderCreateV1 + "'";

    internal DbSet<PurchaseRequestRow> Requests => Set<PurchaseRequestRow>();

    internal DbSet<PurchaseRequestLineRow> RequestLines => Set<PurchaseRequestLineRow>();

    internal DbSet<PurchaseOrderRow> Orders => Set<PurchaseOrderRow>();

    internal DbSet<PurchaseOrderLineRow> OrderLines => Set<PurchaseOrderLineRow>();

    internal DbSet<PurchaseAcceptanceRow> Acceptances => Set<PurchaseAcceptanceRow>();

    internal DbSet<PurchaseAttemptRow> Attempts => Set<PurchaseAttemptRow>();

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
        ConfigureAttempts(modelBuilder);
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
            // A2a-1 (TEK-01): the money snapshot is complete or absent.
            table.HasCheckConstraint("CK_PurchaseOrders_Money", OrderMoneyCheck);
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
        entity.Property(x => x.CurrencyCode).HasMaxLength(PurchaseOrderMoney.MaximumCurrencyCodeLength);
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
            // A2a-1 (ALS-D5, ALS-D18): unpriced, or a general line with a positive price and at most one bounded discount.
            table.HasCheckConstraint("CK_PurchaseOrderLines_Price", OrderLinePriceCheck);
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
                     nameof(PurchaseOrderLineRow.Cancelled),
                 })
        {
            entity.Property<decimal>(quantity).HasPrecision(18, 3);
        }

        // A2a-1: decimal(24,6) holds 18 integer digits and PurchaseOrderMoney.MaximumPriceScale decimals; the domain refuses wider values.
        entity.Property(x => x.UnitPrice).HasPrecision(24, PurchaseOrderMoney.MaximumPriceScale);
        entity.Property(x => x.DiscountAmount).HasPrecision(24, PurchaseOrderMoney.MaximumPriceScale);
        entity.Property(x => x.DiscountPercent).HasPrecision(9, PurchaseOrderLinePrice.PercentScale);

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
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Version", "[OrderVersionAfter] >= 1");
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_PurchaseAcceptanceShares_Text",
                "LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
        });
        entity.HasKey(x => x.AcceptanceId);
        entity.HasAlternateKey(x => new { x.CompanyId, x.AcceptanceId });
        entity.Property(x => x.AcceptanceId).ValueGeneratedNever();
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
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

    private void ApplyWriteGuards()
    {
        PurchasingAuditEventTenantGuard.Validate(this, companyContext);
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
                        nameof(PurchaseOrderRow.CancelledBy), nameof(PurchaseOrderRow.CancelledAtUtc), nameof(PurchaseOrderRow.CancellationReason),
                    ],
                    PurchaseOrderLineRow =>
                        [nameof(PurchaseOrderLineRow.Technical), nameof(PurchaseOrderLineRow.Accepted), nameof(PurchaseOrderLineRow.Cancelled)],
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
