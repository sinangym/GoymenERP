using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the three ID-2r founder recovery types (<c>identity.founder.recovered</c>,
    /// <c>identity.founder.password_reset</c>, <c>identity.founder.recovery_refused</c>; ADR-SEC-001 closed list):
    /// widens CK_SecurityEvents_EventType only. No column and no row is touched. Down refuses (<c>THROW 51851</c>, used
    /// by no other migration) while a founder recovery row exists (forward fix).
    /// </summary>
    public partial class AddFounderRecoverySecurityEvents : Migration
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
                sql: "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read', 'auth.login.rate_limited', 'identity.founder.recovered', 'identity.founder.password_reset', 'identity.founder.recovery_refused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a founder recovery row would violate the narrowed event type check, and security events are
            // append-only. Down refuses while such a row exists; a store without these types rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [security].[SecurityEvents] WHERE [EventType] IN (N'identity.founder.recovered', N'identity.founder.password_reset', N'identity.founder.recovery_refused'))
                BEGIN
                    THROW 51851, 'founder recovery security events exist; AddFounderRecoverySecurityEvents is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents",
                sql: "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read', 'auth.login.rate_limited')");
        }
    }
}
