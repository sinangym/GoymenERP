using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public static class AuditEventModelBuilderExtensions
{
    public static void ConfigureAuditEvent(
        this ModelBuilder modelBuilder,
        bool excludeFromMigrations)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var entity = modelBuilder.Entity<AuditEvent>();
        if (excludeFromMigrations)
        {
            entity.ToTable(
                "AuditEvents",
                "audit",
                table => table.ExcludeFromMigrations());
        }
        else
        {
            entity.ToTable(
                "AuditEvents",
                "audit",
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_AuditEvents_CompanyId_NotEmpty",
                        "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.HasCheckConstraint(
                        "CK_AuditEvents_RoleCodesJson_Valid",
                        "ISJSON([RoleCodesJson]) = 1");
                    table.HasCheckConstraint(
                        "CK_AuditEvents_BeforeJson_Valid",
                        "ISJSON([BeforeJson]) = 1");
                    table.HasCheckConstraint(
                        "CK_AuditEvents_AfterJson_Valid",
                        "ISJSON([AfterJson]) = 1");
                    table.HasCheckConstraint(
                        "CK_AuditEvents_ApprovalJson_Valid",
                        "[ApprovalContextJson] IS NULL OR ISJSON([ApprovalContextJson]) = 1");
                    table.HasCheckConstraint(
                        "CK_AuditEvents_AiJson_Valid",
                        "[AiContextJson] IS NULL OR ISJSON([AiContextJson]) = 1");
                    table.HasCheckConstraint(
                        "CK_AuditEvents_SchemaVersion_Positive",
                        "[SchemaVersion] > 0");
                });
        }

        entity.HasKey(x => x.Id);
        entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
        entity.Property(x => x.EntityType).HasMaxLength(160).IsRequired();
        entity.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Operation).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PermissionCode).HasMaxLength(160).IsRequired();
        entity.Property(x => x.RoleCodesJson).HasMaxLength(2000).IsRequired();
        entity.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.AfterJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ApprovalContextJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.AiInvolvement).HasMaxLength(32).IsRequired();
        entity.Property(x => x.AiContextJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.SourceChannel).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ClientIpAddress).HasMaxLength(64);
        entity.Property(x => x.UserAgent).HasMaxLength(512);
        entity.Property(x => x.CorrelationId)
            .HasMaxLength(128)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        entity.Property(x => x.CausationId)
            .HasMaxLength(128)
            .UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.IdempotencyKeyHash)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        entity.Property(x => x.IntegrityHash)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.OccurredAtUtc, x.Id })
            .IsDescending(false, true, true);
        entity.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        entity.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityId, x.OccurredAtUtc });
    }
}
