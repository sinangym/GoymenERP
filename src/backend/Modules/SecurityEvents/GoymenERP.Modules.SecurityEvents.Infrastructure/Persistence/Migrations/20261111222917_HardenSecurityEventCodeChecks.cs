using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenSecurityEventCodeChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_ActorId",
                schema: "security",
                table: "SecurityEvents",
                sql: "([ActorType] = 'Anonymous' AND [ActorId] IS NULL) OR ([ActorType] <> 'Anonymous' AND [ActorId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_ActorType",
                schema: "security",
                table: "SecurityEvents",
                sql: "[ActorType] IN ('User', 'System', 'Ai', 'Anonymous')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_AiDataClass",
                schema: "security",
                table: "SecurityEvents",
                sql: "[AiDataClass] IS NULL OR [AiDataClass] IN ('Public', 'Internal', 'Confidential', 'StrictlyConfidential')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_AiRiskClass",
                schema: "security",
                table: "SecurityEvents",
                sql: "[AiRiskClass] IS NULL OR [AiRiskClass] IN ('Read', 'Draft', 'Simulate', 'Write')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_ReasonCode",
                schema: "security",
                table: "SecurityEvents",
                sql: "[ReasonCode] IS NULL OR [ReasonCode] IN ('permission_missing', 'company_context_mismatch', 'credentials_invalid', 'account_inactive', 'session_invalid', 'policy_refused', 'ai_tool_not_allowed', 'ai_data_class_not_allowed', 'ai_approval_missing', 'ai_provider_failed', 'operation_failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_Severity",
                schema: "security",
                table: "SecurityEvents",
                sql: "[Severity] IN ('Info', 'Warning', 'High')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_SourceChannel",
                schema: "security",
                table: "SecurityEvents",
                sql: "[SourceChannel] IN ('Web', 'Mobile', 'Import', 'Integration', 'AiAssisted', 'System')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down only drops CHECK constraints: no row loses data or meaning, so no forward-fix guard is needed.
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_ActorId",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_ActorType",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_AiDataClass",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_AiRiskClass",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_ReasonCode",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_Severity",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_SourceChannel",
                schema: "security",
                table: "SecurityEvents");
        }
    }
}
