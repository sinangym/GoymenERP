using GoymenERP.BuildingBlocks.Ai;
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

    /// <summary>
    /// <c>auth.login.rate_limited</c> rows carry the limiter settings, a summary window and a refused count; every other
    /// row carries no limiter column (KIM-D29). Together with CK_SecurityEvents_ScopeCompany and CK_SecurityEvents_ActorId
    /// the database also keeps the summary platform-wide and anonymous.
    /// </summary>
    public const string RateLimitSummaryCheck =
        $"([EventType] = '{SecurityEventTypesV1.LoginRateLimited}' AND [ScopeKind] = '{SecurityEventScopeKindsV1.Platform}' AND [ActorType] = '{SecurityEventActorTypesV1.Anonymous}' AND [RateLimitPermitLimit] IS NOT NULL AND [RateLimitPermitLimit] > 0 AND [RateLimitWindowSeconds] IS NOT NULL AND [RateLimitWindowSeconds] > 0 AND [AggregationWindowStartUtc] IS NOT NULL AND [AggregationWindowEndUtc] IS NOT NULL AND [AggregationWindowStartUtc] < [AggregationWindowEndUtc]) OR ([EventType] <> '{SecurityEventTypesV1.LoginRateLimited}' AND [RateLimitPermitLimit] IS NULL AND [RateLimitWindowSeconds] IS NULL)";

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
                // Defence in depth: the code-list columns accept only the contract values even if a future
                // writer bypassed SecurityEventV1Validator (AI-2a hardening).
                table.HasCheckConstraint("CK_SecurityEvents_ActorType", InList("ActorType", SecurityEventActorTypesV1.All));
                table.HasCheckConstraint(
                    "CK_SecurityEvents_ActorId",
                    $"([ActorType] = '{SecurityEventActorTypesV1.Anonymous}' AND [ActorId] IS NULL) OR ([ActorType] <> '{SecurityEventActorTypesV1.Anonymous}' AND [ActorId] IS NOT NULL)");
                table.HasCheckConstraint("CK_SecurityEvents_Severity", InList("Severity", SecurityEventSeveritiesV1.All));
                table.HasCheckConstraint(
                    "CK_SecurityEvents_SourceChannel", InList("SourceChannel", SecurityEventV1Validator.SourceChannelsV1));
                table.HasCheckConstraint(
                    "CK_SecurityEvents_ReasonCode", NullOrInList("ReasonCode", SecurityEventReasonCodesV1.All));
                table.HasCheckConstraint(
                    "CK_SecurityEvents_AiRiskClass", NullOrInList("AiRiskClass", Enum.GetNames<AiToolRiskClassV1>()));
                table.HasCheckConstraint(
                    "CK_SecurityEvents_AiDataClass", NullOrInList("AiDataClass", Enum.GetNames<AiDataClassV1>()));
                table.HasCheckConstraint("CK_SecurityEvents_RateLimitSummary", RateLimitSummaryCheck);
                table.HasCheckConstraint("CK_SecurityEvents_OperatorTwoFactorRecovery", OperatorTwoFactorRecoveryCheck);
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
            entity.Property(x => x.RateLimitPermitLimit);
            entity.Property(x => x.RateLimitWindowSeconds);
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

    public const string OperatorTwoFactorRecoveryCheck =
        "[EventType] NOT IN ('identity.two_factor.recovered','identity.two_factor.recovery_refused') OR "
        + "([ScopeKind] = 'Platform' AND [CompanyId] IS NULL AND [RequestedCompanyId] IS NULL "
        + "AND [ActorType] = 'System' AND [ActorId] IS NOT NULL AND [ActorId] COLLATE Latin1_General_100_BIN2 = 'identity.recover-two-factor' "
        + "AND [Phase] = 'Single' AND [SourceChannel] = 'System' AND [Severity] = 'High' AND [PermissionCode] IS NULL "
        + "AND [PolicyId] IS NULL AND [PolicyVersion] IS NULL AND [AiToolId] IS NULL AND [AiToolVersion] IS NULL AND [AiRiskClass] IS NULL AND [AiDataClass] IS NULL "
        + "AND [OccurrenceCount] = 1 AND [AggregationWindowStartUtc] IS NULL AND [AggregationWindowEndUtc] IS NULL "
        + "AND (([EventType] = 'identity.two_factor.recovered' AND [Outcome] = 'Allowed' AND [ReasonCode] IS NULL) "
        + "OR ([EventType] = 'identity.two_factor.recovery_refused' AND [Outcome] = 'Denied' AND [ReasonCode] IS NOT NULL AND [ReasonCode] = 'policy_refused')))";

    private static string InList(string column, IEnumerable<string> values) =>
        $"[{column}] IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";

    private static string NullOrInList(string column, IEnumerable<string> values) =>
        $"[{column}] IS NULL OR {InList(column, values)}";
}
