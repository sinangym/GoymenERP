using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// Purchasing's own tables in the <c>purchasing</c> schema (PUR-4a; ADR-002, ADR-009, ADR-011; the Sales
/// SALES-P2 / Logistics LOG-4a shape). Every row is a tenant row of the record's company: a global query
/// filter on the server-resolved company and a write guard that refuses any row of another company, any
/// company change, any delete and any column move outside the axis a command may move (a header's status /
/// version / time, a request's sourcing version; an order line's technical and accepted totals; attempts append
/// only). The attempt key (company, command, key hash) is a primary key under a binary collation; the raw key
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

    /// <summary>One create attempt per stored order; the approve command's attempts share the order id.</summary>
    internal static readonly string OrderCreateAttemptFilter =
        "[OrderId] IS NOT NULL AND [CommandName] = N'" + PurchasingCommands.OrderCreateV1 + "'";

    internal DbSet<PurchaseRequestRow> Requests => Set<PurchaseRequestRow>();

    internal DbSet<PurchaseRequestLineRow> RequestLines => Set<PurchaseRequestLineRow>();

    internal DbSet<PurchaseOrderRow> Orders => Set<PurchaseOrderRow>();

    internal DbSet<PurchaseOrderLineRow> OrderLines => Set<PurchaseOrderLineRow>();

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
            table.HasCheckConstraint("CK_PurchaseRequests_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_PurchaseRequests_Version", "[Version] >= 1 AND [SourcingVersion] >= 0");
            table.HasCheckConstraint("CK_PurchaseRequests_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.EffectiveOn).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
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
        });
        entity.HasKey(x => new { x.CompanyId, x.RequestId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.RequestId, x.LineId }).IsUnique().HasDatabaseName("UX_PurchaseRequestLines_LineId");
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
            table.HasCheckConstraint("CK_PurchaseOrders_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_PurchaseOrders_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_PurchaseOrders_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.EffectiveOn).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
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
            // §7.2 / §13: technical slices and the accepted share never exceed the first commitment.
            table.HasCheckConstraint("CK_PurchaseOrderLines_Shares",
                "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Technical] + [Accepted] <= [Ordered]");
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.LineId }).IsUnique().HasDatabaseName("UX_PurchaseOrderLines_LineId");
        entity.HasIndex(x => new { x.CompanyId, x.SourceRequestLineId }).HasDatabaseName("IX_PurchaseOrderLines_SourceRequestLine");
        entity.Property(x => x.ProductVersion).HasMaxLength(64).IsRequired();
        foreach (var quantity in new[] { nameof(PurchaseOrderLineRow.Ordered), nameof(PurchaseOrderLineRow.Technical), nameof(PurchaseOrderLineRow.Accepted) })
        {
            entity.Property<decimal>(quantity).HasPrecision(18, 3);
        }

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
                    ],
                    PurchaseOrderRow => [nameof(PurchaseOrderRow.Status), nameof(PurchaseOrderRow.UpdatedAtUtc)],
                    PurchaseOrderLineRow => [nameof(PurchaseOrderLineRow.Technical), nameof(PurchaseOrderLineRow.Accepted)],
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
