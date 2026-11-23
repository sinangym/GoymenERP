using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// WP4 (SAT-D475 / SAT-D476): one approval request of a Sales record (<c>sales.ApprovalRequests</c>). The request facts never
/// change; the decision columns move once, Requested → Approved | Rejected (the write guard and the checks). The subject is the
/// quote (kind 1) or the order (kind 2); the Logistics instruction and shipment are opaque references Sales never reads.
/// </summary>
internal sealed class ApprovalRequestRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public int Kind { get; set; }
    public Guid SubjectId { get; set; }
    public int RequestNo { get; set; }
    public int SubjectRevision { get; set; }
    public Guid? LineId { get; set; }
    public string? Ordered { get; set; }
    public string? Quantity { get; set; }
    public string? RequestedTotal { get; set; }
    public Guid? SourceInstructionId { get; set; }
    public Guid? SourceShipmentId { get; set; }
    public string? Note { get; set; }
    public int State { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>WP4 (ADR-011 §2–§8): the approval request commands' own attempt records (append-only).</summary>
internal sealed class ApprovalRequestAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored request or decision (<see cref="RequestId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set).</summary>
    public int Outcome { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? RequestId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class SalesApprovalRequestRows
{
    public static ApprovalRequestRow ToRow(SalesApprovalRequest item, string correlationId) => new()
    {
        Id = item.Id,
        CompanyId = item.CompanyId,
        Kind = (int)item.Kind,
        SubjectId = item.SubjectId,
        RequestNo = item.RequestNo,
        SubjectRevision = item.SubjectRevision,
        LineId = item.LineId,
        Ordered = item.Ordered,
        Quantity = item.Quantity,
        RequestedTotal = item.RequestedTotal,
        SourceInstructionId = item.SourceInstructionId,
        SourceShipmentId = item.SourceShipmentId,
        Note = item.Note,
        State = (int)item.State,
        RequestedBy = item.RequestedBy,
        RequestedAtUtc = item.RequestedAtUtc,
        DecidedBy = item.Decision?.DecidedBy,
        DecidedAtUtc = item.Decision?.DecidedAtUtc,
        DecisionReason = item.Decision?.Reason,
        Version = item.Version,
        UpdatedAtUtc = item.UpdatedAtUtc,
        CorrelationId = correlationId.Trim(),
    };

    public static SalesApprovalRequest Restore(ApprovalRequestRow row)
    {
        if (!Enum.IsDefined(typeof(SalesApprovalRequestKind), row.Kind) || !Enum.IsDefined(typeof(SalesApprovalRequestState), row.State))
        {
            throw new InvalidOperationException("Kayıtlı onay talebinin türü ya da durumu tanınmıyor.");
        }

        var item = SalesApprovalRequest.Rebuild(row.Id, row.CompanyId, (SalesApprovalRequestKind)row.Kind, row.SubjectId, row.RequestNo,
            row.SubjectRevision, row.LineId, row.Ordered, row.Quantity, row.RequestedTotal, row.SourceInstructionId, row.SourceShipmentId,
            row.Note, row.RequestedBy, row.RequestedAtUtc);
        var decision = row.DecidedBy is { } by && row.DecidedAtUtc is { } at
            ? new SalesApprovalRequestDecision(by, at, row.DecisionReason)
            : row.DecidedBy is null && row.DecidedAtUtc is null && row.DecisionReason is null
                ? null
                : throw new InvalidOperationException("Kayıtlı onay talebi kararı eksik.");
        item.Restore((SalesApprovalRequestState)row.State, decision, row.Version, row.UpdatedAtUtc);
        return item;
    }

    /// <summary>Writes the one decision onto the tracked row: the decision columns, the version and the time only.</summary>
    public static void ApplyDecision(ApprovalRequestRow row, SalesApprovalRequest item)
    {
        row.State = (int)item.State;
        row.DecidedBy = item.Decision?.DecidedBy;
        row.DecidedAtUtc = item.Decision?.DecidedAtUtc;
        row.DecisionReason = item.Decision?.Reason;
        row.Version = item.Version;
        row.UpdatedAtUtc = item.UpdatedAtUtc;
    }
}

public sealed partial class SalesDbContext
{
    /// <summary>CK_ApprovalRequests_Facts: a quote request names no line, quantity or Logistics reference; an over-delivery request names all.</summary>
    internal const string ApprovalRequestFactsConstraint =
        "([Kind] = 1 AND [LineId] IS NULL AND [Ordered] IS NULL AND [Quantity] IS NULL AND [RequestedTotal] IS NULL "
        + "AND [SourceInstructionId] IS NULL AND [SourceShipmentId] IS NULL AND ([Note] IS NULL OR LEN([Note]) > 0)) "
        + "OR ([Kind] = 2 AND [LineId] IS NOT NULL AND [LineId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [Ordered] IS NOT NULL AND LEN([Ordered]) > 0 AND [Quantity] IS NOT NULL AND LEN([Quantity]) > 0 "
        + "AND [RequestedTotal] IS NOT NULL AND LEN([RequestedTotal]) > 0 "
        + "AND [SourceInstructionId] IS NOT NULL AND [SourceInstructionId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [SourceShipmentId] IS NOT NULL AND [SourceShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [Note] IS NULL)";

    /// <summary>CK_ApprovalRequests_Decision: Requested (1) none; Approved (2, over-delivery only) without a reason; Rejected (3) with one.</summary>
    internal const string ApprovalRequestDecisionConstraint =
        "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) "
        + "OR ([State] = 2 AND [Kind] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NULL) "
        + "OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL "
        + "AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0)";

    /// <summary>CK_ApprovalRequests_MakerChecker (SAT-D216): the shipper never decides an over-delivery request.</summary>
    internal const string ApprovalRequestMakerCheckerConstraint =
        "[Kind] <> 2 OR [DecidedBy] IS NULL OR [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [RequestedBy] COLLATE Latin1_General_100_BIN2";

    /// <summary>UX_ApprovalRequests_PendingQuoteRevision: at most one pending conversion request per quote revision.</summary>
    internal const string PendingQuoteConversionRequestFilter = "[Kind] = 1 AND [State] = 1";

    /// <summary>UX_ApprovalRequests_PendingOrderLine: at most one pending over-delivery request per order line.</summary>
    internal const string PendingOverDeliveryRequestFilter = "[Kind] = 2 AND [State] = 1";

    /// <summary>CK_ApprovalRequestAttempts_Command: only the approval request's own versioned commands record here.</summary>
    internal const string ApprovalRequestAttemptCommandConstraint =
        "[CommandName] IN (N'" + SalesApprovalRequestCommands.RequestQuoteConversionV1 + "', N'"
        + SalesApprovalRequestCommands.RejectQuoteConversionV1 + "', N'" + SalesApprovalRequestCommands.ApproveOverDeliveryV1 + "', N'"
        + SalesApprovalRequestCommands.RejectOverDeliveryV1 + "')";

    /// <summary>CK_ApprovalRequestAttempts_Outcome: a recorded attempt names its request; a refusal carries its JSON.</summary>
    internal const string ApprovalRequestAttemptOutcomeConstraint =
        "([Outcome] = 1 AND [RequestId] IS NOT NULL AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND [RequestId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";

    /// <summary>WP4: the approval requests (SAT-D475 / SAT-D476).</summary>
    internal DbSet<ApprovalRequestRow> ApprovalRequests => Set<ApprovalRequestRow>();

    /// <summary>WP4: the approval request commands' own attempt records (append-only).</summary>
    internal DbSet<ApprovalRequestAttemptRow> ApprovalRequestAttempts => Set<ApprovalRequestAttemptRow>();

    private void ConfigureApprovalRequests(ModelBuilder modelBuilder)
    {
        var request = modelBuilder.Entity<ApprovalRequestRow>();
        request.ToTable("ApprovalRequests", Schema, table =>
        {
            table.HasCheckConstraint("CK_ApprovalRequests_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SubjectId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1, 2) AND [RequestNo] >= 1 "
                + "AND [SubjectRevision] >= 0 AND [Version] >= 1");
            table.HasCheckConstraint("CK_ApprovalRequests_State", "[State] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_ApprovalRequests_Facts", ApprovalRequestFactsConstraint);
            table.HasCheckConstraint("CK_ApprovalRequests_Decision", ApprovalRequestDecisionConstraint);
            table.HasCheckConstraint("CK_ApprovalRequests_MakerChecker", ApprovalRequestMakerCheckerConstraint);
            table.HasCheckConstraint("CK_ApprovalRequests_Text", "LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        request.HasKey(x => x.Id);
        request.HasAlternateKey(x => new { x.CompanyId, x.Id });
        request.Property(x => x.Id).ValueGeneratedNever();
        request.Property(x => x.Ordered).HasMaxLength(SalesApprovalRequest.MaximumQuantityTextLength);
        request.Property(x => x.Quantity).HasMaxLength(SalesApprovalRequest.MaximumQuantityTextLength);
        request.Property(x => x.RequestedTotal).HasMaxLength(SalesApprovalRequest.MaximumQuantityTextLength);
        request.Property(x => x.Note).HasMaxLength(SalesApprovalRequest.MaximumTextLength);
        request.Property(x => x.RequestedBy).HasMaxLength(SalesApprovalRequest.MaximumActorLength).IsRequired();
        request.Property(x => x.DecidedBy).HasMaxLength(SalesApprovalRequest.MaximumActorLength);
        request.Property(x => x.DecisionReason).HasMaxLength(SalesApprovalRequest.MaximumTextLength);
        request.Property(x => x.Version).IsConcurrencyToken();
        request.Property(x => x.RowVersion).IsRowVersion();
        request.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        request.HasIndex(x => new { x.CompanyId, x.Kind, x.SubjectId, x.RequestNo }).IsUnique().HasDatabaseName("UX_ApprovalRequests_No");
        request.HasIndex(x => new { x.CompanyId, x.SubjectId, x.SubjectRevision }).IsUnique()
            .HasFilter(PendingQuoteConversionRequestFilter).HasDatabaseName("UX_ApprovalRequests_PendingQuoteRevision");
        request.HasIndex(x => new { x.CompanyId, x.SubjectId, x.LineId }).IsUnique()
            .HasFilter(PendingOverDeliveryRequestFilter).HasDatabaseName("UX_ApprovalRequests_PendingOrderLine");
        request.HasIndex(x => new { x.CompanyId, x.Kind, x.State, x.RequestedAtUtc }).HasDatabaseName("IX_ApprovalRequests_Pending");
        request.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        request.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<ApprovalRequestAttemptRow>();
        attempt.ToTable("ApprovalRequestAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ApprovalRequestAttempts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SubjectId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_ApprovalRequestAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_ApprovalRequestAttempts_Command", ApprovalRequestAttemptCommandConstraint);
            table.HasCheckConstraint("CK_ApprovalRequestAttempts_Outcome", ApprovalRequestAttemptOutcomeConstraint);
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<ApprovalRequestRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
