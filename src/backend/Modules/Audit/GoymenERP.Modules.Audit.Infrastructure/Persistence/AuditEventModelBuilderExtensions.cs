using GoymenERP.Modules.Audit.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence;

public static partial class AuditEventModelBuilderExtensions
{
    // Null permission is reserved for the typed, successful founder CLI command. Existing User/AI/Integration/System
    // permission-bearing events retain their invariant; a NULL/UNKNOWN JSON comparison cannot admit another variant.
    internal const string FounderRecoveryPermissionCheck =
        "([PermissionCode] IS NOT NULL AND LEN(LTRIM(RTRIM([PermissionCode]))) > 0) OR "
        + "([PermissionCode] IS NULL AND [SubjectKind] = N'Company' AND [SchemaVersion] = 1 "
        + "AND [ActorType] COLLATE Latin1_General_100_BIN2 = N'System' AND [ActorId] COLLATE Latin1_General_100_BIN2 = N'identity.recover-founder' "
        + "AND [EntityType] COLLATE Latin1_General_100_BIN2 = N'Identity.User' AND TRY_CONVERT(uniqueidentifier,[EntityId]) IS NOT NULL "
        + "AND TRY_CONVERT(uniqueidentifier,[EntityId]) <> '00000000-0000-0000-0000-000000000000' "
        + "AND (([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.recovered' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'RecoverFounder') "
        + "OR ([EventType] COLLATE Latin1_General_100_BIN2 = N'identity.founder.password_reset' AND [Operation] COLLATE Latin1_General_100_BIN2 = N'ResetFounderPassword')) "
        + "AND [SourceChannel] COLLATE Latin1_General_100_BIN2 = N'System' AND [Outcome] COLLATE Latin1_General_100_BIN2 = N'Succeeded' "
        + "AND [EntityVersionBefore] IS NOT NULL AND [EntityVersionBefore] >= 1 AND [EntityVersionAfter] IS NOT NULL AND [EntityVersionAfter] >= [EntityVersionBefore] "
        + "AND [RoleCodesJson] = N'[]' AND [AiInvolvement] = N'None' AND [AiContextJson] IS NULL "
        + "AND [ClientIpAddress] IS NULL AND [UserAgent] IS NULL AND [ApprovalContextJson] IS NOT NULL AND ISJSON([ApprovalContextJson]) = 1 "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') IS NOT NULL "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.kind') COLLATE Latin1_General_100_BIN2 = N'IdentityFounderRecoveryV1' "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser') IS NOT NULL "
        + "AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.operatingSystemUser')))) BETWEEN 1 AND 256 "
        + "AND JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName') IS NOT NULL "
        + "AND LEN(LTRIM(RTRIM(JSON_VALUE(CASE WHEN ISJSON([ApprovalContextJson]) = 1 THEN [ApprovalContextJson] ELSE N'{}' END, '$.machineName')))) BETWEEN 1 AND 256)";
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
                    table.HasCheckConstraint("CK_AuditEvents_Subject",
                        CompanyAndProductSubjectCheck + " OR " + OperatorRecoveryPermissionCheck);
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
                    table.HasCheckConstraint("CK_AuditEvents_PermissionOrFounderRecovery", TypedPermissionCheck);
                });
        }

        entity.HasKey(x => x.Id);
        entity.Property(x => x.CompanyId).IsRequired(false);
        entity.Property(x => x.SubjectKind).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2")
            .HasDefaultValue(AuditEvent.CompanySubject).IsRequired();
        entity.Property(x => x.AuthorityRevision).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
        entity.Property(x => x.EntityType).HasMaxLength(160).IsRequired();
        entity.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Operation).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        // Legacy command contexts map only permission-bearing audit drafts and do not own this table's DDL.
        // Keep their reviewed required mapping unchanged; only Audit owns the new typed System-null variant.
        entity.Property(x => x.PermissionCode).HasMaxLength(160).IsRequired(excludeFromMigrations);
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
        // F023-5a: the outcome-filtered company read (e.g. only refused attempts) keeps the same
        // (OccurredAtUtc DESC, Id DESC) keyset as the unfiltered read above.
        entity.HasIndex(x => new { x.CompanyId, x.Outcome, x.OccurredAtUtc, x.Id })
            .IsDescending(false, false, true, true);
        entity.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityId, x.OccurredAtUtc });
        entity.HasIndex(x => new { x.SubjectKind, x.EntityType, x.EntityId, x.OccurredAtUtc, x.Id });
    }
}
