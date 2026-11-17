using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

internal sealed class OfficialInvoiceCancellationRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CancellationId { get; set; }
    public Guid InvoiceId { get; set; }
    public int Kind { get; set; }
    public Guid? NormalInvoiceId { get; set; }
    public Guid? SubcontractInvoiceId { get; set; }
    public long FinalizedVersion { get; set; }
    public long InvoiceVersion { get; set; }
    public Guid AdmissionId { get; set; }
    public string ActorId { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string ViewJson { get; set; } = null!;
    public string AdmissionJson { get; set; } = null!;
}
internal sealed class OfficialInvoiceCancellationAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = null!;
    public string KeyHash { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public string ActorId { get; set; } = null!;
    public Guid? CancellationId { get; set; }
    public string CorrelationId { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string? ResultJson { get; set; }
    public string? RefusalJson { get; set; }
}
internal sealed class OfficialInvoiceCancellationDeliveryRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CancellationId { get; set; }
    public long Sequence { get; set; }
    public string ActorId { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public bool ReleaseActiveAllocations { get; set; }
    public string ViewJson { get; set; } = null!;
}
public sealed partial class SalesDbContext
{
    internal DbSet<OfficialInvoiceCancellationRow> OfficialInvoiceCancellations => Set<OfficialInvoiceCancellationRow>();
    internal DbSet<OfficialInvoiceCancellationAttemptRow> OfficialInvoiceCancellationAttempts => Set<OfficialInvoiceCancellationAttemptRow>();
    internal DbSet<OfficialInvoiceCancellationDeliveryRow> OfficialInvoiceCancellationDeliveries => Set<OfficialInvoiceCancellationDeliveryRow>();

    private void ConfigureOfficialInvoiceCancellations(ModelBuilder modelBuilder)
    {
        var facts = modelBuilder.Entity<OfficialInvoiceCancellationRow>();
        facts.ToTable("OfficialInvoiceCancellations", Schema, t =>
        {
            t.HasCheckConstraint("CK_OfficialInvoiceCancellations_Identity", "[CancellationId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0 AND [InvoiceVersion] = [FinalizedVersion] + 1 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
            t.HasCheckConstraint("CK_OfficialInvoiceCancellations_Source", "([Kind] = 1 AND [NormalInvoiceId] = [InvoiceId] AND [NormalInvoiceId] IS NOT NULL AND [SubcontractInvoiceId] IS NULL) OR ([Kind] = 2 AND [SubcontractInvoiceId] = [InvoiceId] AND [SubcontractInvoiceId] IS NOT NULL AND [NormalInvoiceId] IS NULL)");
            t.HasCheckConstraint("CK_OfficialInvoiceCancellations_Json", "ISJSON([ViewJson]) = 1 AND ISJSON([AdmissionJson]) = 1");
            t.HasTrigger("TR_OfficialInvoiceCancellations_Immutable"); t.UseSqlOutputClause(false);
        });
        facts.HasKey(x => new { x.CompanyId, x.CancellationId });
        facts.HasIndex(x => new { x.CompanyId, x.Kind, x.InvoiceId }).IsUnique();
        facts.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        facts.Property(x => x.CorrelationId).HasMaxLength(128);
        facts.HasOne<InvoiceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.NormalInvoiceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        facts.HasOne<SubcontractServiceInvoiceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SubcontractInvoiceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        facts.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<OfficialInvoiceCancellationAttemptRow>();
        attempt.ToTable("OfficialInvoiceCancellationAttempts", Schema, t =>
        {
            t.HasCheckConstraint("CK_OfficialInvoiceCancellationAttempts_Identity", "LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND [CommandName] IN (N'sales.invoice.official-cancellation.record.v1', N'sales.invoice.official-cancellation.finance-retry.v1')");
            t.HasCheckConstraint("CK_OfficialInvoiceCancellationAttempts_Outcome", "([CancellationId] IS NOT NULL AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1 AND [RefusalJson] IS NULL) OR ([ResultJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
            t.HasTrigger("TR_OfficialInvoiceCancellationAttempts_Immutable"); t.UseSqlOutputClause(false);
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.KeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(x => x.CorrelationId).HasMaxLength(128);
        attempt.HasOne<OfficialInvoiceCancellationRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CancellationId }).OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var delivery = modelBuilder.Entity<OfficialInvoiceCancellationDeliveryRow>();
        delivery.ToTable("OfficialInvoiceCancellationDeliveries", Schema, t =>
        {
            t.HasCheckConstraint("CK_OfficialInvoiceCancellationDeliveries_Identity", "[Sequence] > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ViewJson]) = 1");
            t.HasTrigger("TR_OfficialInvoiceCancellationDeliveries_Immutable"); t.UseSqlOutputClause(false);
        });
        delivery.HasKey(x => new { x.CompanyId, x.CancellationId, x.Sequence });
        delivery.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        delivery.Property(x => x.CorrelationId).HasMaxLength(128);
        delivery.HasOne<OfficialInvoiceCancellationRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CancellationId }).OnDelete(DeleteBehavior.Restrict);
        delivery.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
