using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorTwoFactorRecoverySecurityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents",
                sql: "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read', 'auth.login.rate_limited', 'identity.founder.recovered', 'identity.founder.password_reset', 'identity.founder.recovery_refused', 'identity.two_factor.recovered', 'identity.two_factor.recovery_refused')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_OperatorTwoFactorRecovery",
                schema: "security",
                table: "SecurityEvents",
                sql: "[EventType] NOT IN ('identity.two_factor.recovered','identity.two_factor.recovery_refused') OR ([ScopeKind] = 'Platform' AND [CompanyId] IS NULL AND [RequestedCompanyId] IS NULL AND [ActorType] = 'System' AND [ActorId] IS NOT NULL AND [ActorId] COLLATE Latin1_General_100_BIN2 = 'identity.recover-two-factor' AND [Phase] = 'Single' AND [SourceChannel] = 'System' AND [Severity] = 'High' AND [PermissionCode] IS NULL AND [PolicyId] IS NULL AND [PolicyVersion] IS NULL AND [AiToolId] IS NULL AND [AiToolVersion] IS NULL AND [AiRiskClass] IS NULL AND [AiDataClass] IS NULL AND [OccurrenceCount] = 1 AND [AggregationWindowStartUtc] IS NULL AND [AggregationWindowEndUtc] IS NULL AND (([EventType] = 'identity.two_factor.recovered' AND [Outcome] = 'Allowed' AND [ReasonCode] IS NULL) OR ([EventType] = 'identity.two_factor.recovery_refused' AND [Outcome] = 'Denied' AND [ReasonCode] IS NOT NULL AND [ReasonCode] = 'policy_refused')))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [security].[SecurityEvents]
                    WHERE [EventType] IN ('identity.two_factor.recovered','identity.two_factor.recovery_refused'))
                BEGIN
                    THROW 51924, 'Operator two-factor recovery security history exists; AddOperatorTwoFactorRecoverySecurityEvents requires a forward fix.', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_OperatorTwoFactorRecovery",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents",
                sql: "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read', 'auth.login.rate_limited', 'identity.founder.recovered', 'identity.founder.password_reset', 'identity.founder.recovery_refused')");
        }
    }
}
