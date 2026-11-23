using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

internal sealed class SubcontractInvoiceLifecycleRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public long Sequence { get; set; }
    public long InvoiceVersion { get; set; }
    public int State { get; set; }
    public string FactsJson { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class OfficialInvoiceClaimRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public string SourceKind { get; set; } = null!;
    public string OfficialInvoiceNo { get; set; } = null!;
    public string Ettn { get; set; } = null!;
}

internal sealed class SubcontractInvoiceCommandAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = null!;
    public string IdempotencyKeyHash { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string ActorId { get; set; } = null!;
    public Guid? InvoiceId { get; set; }
    public long? LifecycleSequence { get; set; }
    public int? RefusalStatus { get; set; }
    public string? RefusalCode { get; set; }
    public string? RefusalErrorsJson { get; set; }
    public string CorrelationId { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class SalesDbContext
{
    internal const string SubcontractLifecycleIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [Sequence] >= 1 AND [InvoiceVersion] >= 1 AND [State] IN (1,2,3,4,5,6,7) AND ISJSON([FactsJson]) = 1 AND LEN([CorrelationId]) > 0";
    internal const string OfficialInvoiceClaimIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [SourceKind] IN (N'dispatch',N'subcontract') AND LEN([OfficialInvoiceNo]) > 0 AND LEN([Ettn]) = 36";
    internal const string SubcontractCommandOutcomeCheck = "([RefusalStatus] IS NULL AND [RefusalCode] IS NULL AND [RefusalErrorsJson] IS NULL AND [InvoiceId] IS NOT NULL) OR ([RefusalStatus] IS NOT NULL AND [RefusalStatus] IN (400,403,404,409,422) AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0 AND [RefusalErrorsJson] IS NOT NULL AND ISJSON([RefusalErrorsJson]) = 1)";
    internal const string SubcontractCommandSequenceCheck = "[LifecycleSequence] IS NULL OR ([LifecycleSequence] >= 1 AND [RefusalStatus] IS NULL AND [InvoiceId] IS NOT NULL)";
    internal DbSet<SubcontractInvoiceLifecycleRow> SubcontractInvoiceLifecycles => Set<SubcontractInvoiceLifecycleRow>();
    internal DbSet<OfficialInvoiceClaimRow> OfficialInvoiceClaims => Set<OfficialInvoiceClaimRow>();
    internal DbSet<SubcontractInvoiceCommandAttemptRow> SubcontractInvoiceCommandAttempts => Set<SubcontractInvoiceCommandAttemptRow>();

    private void ConfigureSubcontractLifecycle(ModelBuilder modelBuilder)
    {
        var lifecycle = modelBuilder.Entity<SubcontractInvoiceLifecycleRow>();
        lifecycle.ToTable("SubcontractInvoiceLifecycles", Schema, table =>
        {
            table.HasCheckConstraint("CK_SubcontractInvoiceLifecycles_Identity", SubcontractLifecycleIdentityCheck);
            table.HasTrigger("TR_SubcontractInvoiceLifecycles_Immutable");
            table.UseSqlOutputClause(false);
        });
        lifecycle.HasKey(item => new { item.CompanyId, item.InvoiceId, item.Sequence });
        lifecycle.Property(item => item.FactsJson).IsRequired();
        lifecycle.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        lifecycle.HasOne<SubcontractServiceInvoiceRow>().WithMany()
            .HasForeignKey(item => new { item.CompanyId, item.InvoiceId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        lifecycle.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);

        var claim = modelBuilder.Entity<OfficialInvoiceClaimRow>();
        claim.ToTable("OfficialInvoiceClaims", Schema, table =>
        {
            table.HasCheckConstraint("CK_OfficialInvoiceClaims_Identity", OfficialInvoiceClaimIdentityCheck);
            table.HasTrigger("TR_OfficialInvoiceClaims_Immutable");
            table.UseSqlOutputClause(false);
        });
        claim.HasKey(item => new { item.CompanyId, item.Ettn });
        claim.Property(item => item.Ettn).HasMaxLength(SalesInvoice.EttnLength).IsUnicode(false).UseCollation(IdentityCollation);
        claim.Property(item => item.OfficialInvoiceNo).HasMaxLength(SalesInvoice.MaximumOfficialNoLength).UseCollation(IdentityCollation);
        claim.Property(item => item.SourceKind).HasMaxLength(16).UseCollation(IdentityCollation);
        claim.HasIndex(item => new { item.CompanyId, item.OfficialInvoiceNo }).IsUnique();
        claim.HasIndex(item => new { item.CompanyId, item.InvoiceId, item.SourceKind }).IsUnique();
        claim.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        claim.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<SubcontractInvoiceCommandAttemptRow>();
        attempt.ToTable("SubcontractInvoiceCommandAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_SubcontractInvoiceCommandAttempts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_SubcontractInvoiceCommandAttempts_Outcome", SubcontractCommandOutcomeCheck);
            table.HasCheckConstraint("CK_SubcontractInvoiceCommandAttempts_Sequence", SubcontractCommandSequenceCheck);
            table.HasTrigger("TR_SubcontractInvoiceCommandAttempts_Immutable");
            table.UseSqlOutputClause(false);
        });
        attempt.HasKey(item => new { item.CompanyId, item.CommandName, item.IdempotencyKeyHash });
        attempt.Property(item => item.CommandName).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(item => item.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(item => item.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(item => item.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(item => item.CorrelationId).HasMaxLength(128);
        attempt.Property(item => item.RefusalCode).HasMaxLength(128);
        attempt.HasOne<SubcontractServiceInvoiceRow>().WithMany()
            .HasForeignKey(item => new { item.CompanyId, item.InvoiceId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);
    }
}
