using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.SecurityEvents.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence;

/// <summary>
/// The shared <c>security.SecurityEvents</c> mapping. An owner module that needs the same SQL transaction may
/// later map it with <c>excludeFromMigrations: true</c> (ADR-SEC-001 §2; follow-up, not in AI-2a).
/// </summary>
public static class SecurityEventModelBuilderExtensions
{
    public const string Schema = "security";
    public const string TableName = "SecurityEvents";
    public const string AppendOnlyTriggerName = "TR_SecurityEvents_RejectUpdateDelete";

    public static ModelBuilder ConfigureSecurityEvent(this ModelBuilder modelBuilder, bool excludeFromMigrations)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SecurityEventRecord>(entity =>
        {
            entity.ToTable(TableName, Schema, table =>
            {
                if (excludeFromMigrations)
                {
                    table.ExcludeFromMigrations();
                }

                table.HasCheckConstraint(
                    "CK_SecurityEvents_ScopeCompany",
                    $"([ScopeKind] = '{SecurityEventScopeKindsV1.Company}' AND [CompanyId] IS NOT NULL) OR ([ScopeKind] = '{SecurityEventScopeKindsV1.Platform}' AND [CompanyId] IS NULL)");
                table.HasCheckConstraint("CK_SecurityEvents_LegalHold", "[LegalHold] = 0");
                table.HasCheckConstraint("CK_SecurityEvents_EventType", InList("EventType", SecurityEventTypesV1.All));
                table.HasCheckConstraint("CK_SecurityEvents_Phase", InList("Phase", SecurityEventPhasesV1.All));
                table.HasCheckConstraint(
                    "CK_SecurityEvents_PhaseEventType",
                    $"([EventType] = '{SecurityEventTypesV1.AiToolCalled}' AND [Phase] IN ('{SecurityEventPhasesV1.Started}', '{SecurityEventPhasesV1.Completed}')) OR ([EventType] <> '{SecurityEventTypesV1.AiToolCalled}' AND [Phase] = '{SecurityEventPhasesV1.Single}')");
                table.HasCheckConstraint("CK_SecurityEvents_Outcome", InList("Outcome", SecurityEventOutcomesV1.All));
                table.HasCheckConstraint("CK_SecurityEvents_RetainUntil", "[RetainUntilUtc] > [OccurredAtUtc]");
                table.HasCheckConstraint("CK_SecurityEvents_OccurrenceCount", "[OccurrenceCount] >= 1");
                table.HasCheckConstraint(
                    "CK_SecurityEvents_AggregationWindow",
                    "([AggregationWindowStartUtc] IS NULL AND [AggregationWindowEndUtc] IS NULL AND [OccurrenceCount] = 1) OR ([AggregationWindowStartUtc] IS NOT NULL AND [AggregationWindowEndUtc] IS NOT NULL AND [AggregationWindowStartUtc] <= [AggregationWindowEndUtc])");
            });

            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.SchemaVersion).IsRequired();
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            Code(entity.Property(x => x.ScopeKind), SecurityEventRecord.CodeMaxLength).IsRequired();
            Code(entity.Property(x => x.ActorType), SecurityEventRecord.CodeMaxLength).IsRequired();
            Code(entity.Property(x => x.ActorId), SecurityEventV1.ActorIdMaxLength);
            Code(entity.Property(x => x.EventType), SecurityEventRecord.EventTypeMaxLength).IsRequired();
            Code(entity.Property(x => x.Phase), SecurityEventRecord.CodeMaxLength).IsRequired();
            Code(entity.Property(x => x.Outcome), SecurityEventRecord.CodeMaxLength).IsRequired();
            Code(entity.Property(x => x.ReasonCode), SecurityEventRecord.ReasonCodeMaxLength);
            Code(entity.Property(x => x.Severity), SecurityEventRecord.CodeMaxLength).IsRequired();
            Code(entity.Property(x => x.PermissionCode), SecurityEventV1.PermissionCodeMaxLength);
            Code(entity.Property(x => x.PolicyId), SecurityEventV1.PolicyIdMaxLength);
            Code(entity.Property(x => x.CorrelationId), SecurityEventV1.CorrelationIdMaxLength)
                .IsRequired()
                .UseCollation("Latin1_General_100_BIN2");
            Code(entity.Property(x => x.CausationId), SecurityEventV1.CorrelationIdMaxLength);
            Code(entity.Property(x => x.SourceChannel), SecurityEventRecord.CodeMaxLength).IsRequired();
            Code(entity.Property(x => x.AiToolId), SecurityEventV1.AiToolIdMaxLength);
            Code(entity.Property(x => x.AiToolVersion), SecurityEventV1.AiToolVersionMaxLength);
            Code(entity.Property(x => x.AiRiskClass), SecurityEventRecord.CodeMaxLength);
            Code(entity.Property(x => x.AiDataClass), SecurityEventRecord.CodeMaxLength);
            entity.Property(x => x.RetainUntilUtc).IsRequired();
            Code(entity.Property(x => x.RetentionPolicyRef), SecurityEventRecord.RetentionPolicyRefMaxLength).IsRequired();
            entity.Property(x => x.LegalHold).IsRequired();
            entity.Property(x => x.OccurrenceCount).IsRequired().HasDefaultValue(1);
            entity.Property(x => x.AggregationWindowStartUtc);
            entity.Property(x => x.AggregationWindowEndUtc);
            entity.Property(x => x.IntegrityHash)
                .HasMaxLength(SecurityEventRecord.IntegrityHashLength)
                .IsFixedLength()
                .IsUnicode(false)
                .IsRequired();

            entity.HasIndex(x => new { x.CompanyId, x.OccurredAtUtc })
                .HasDatabaseName("IX_SecurityEvents_CompanyId_OccurredAtUtc")
                .IsDescending(false, true);
            entity.HasIndex(x => x.CorrelationId)
                .HasDatabaseName("IX_SecurityEvents_CorrelationId");
        });

        return modelBuilder;
    }

    private static PropertyBuilder<T> Code<T>(PropertyBuilder<T> property, int maxLength) =>
        property.HasMaxLength(maxLength).IsUnicode(false);

    private static string InList(string column, IEnumerable<string> values) =>
        $"[{column}] IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
