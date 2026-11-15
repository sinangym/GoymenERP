using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>auth.login.rate_limited</c> summary type (KIM-D29): widens CK_SecurityEvents_EventType, adds the two
    /// nullable limiter columns (permit limit, window seconds) and CK_SecurityEvents_RateLimitSummary. No row is
    /// touched. Down refuses (<c>THROW 51591</c>, used by no other migration) while a summary row exists (forward fix).
    /// </summary>
    public partial class AddLoginRateLimitedSecurityEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.AddColumn<int>(
                name: "RateLimitPermitLimit",
                schema: "security",
                table: "SecurityEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RateLimitWindowSeconds",
                schema: "security",
                table: "SecurityEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents",
                sql: "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read', 'auth.login.rate_limited')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_RateLimitSummary",
                schema: "security",
                table: "SecurityEvents",
                sql: "([EventType] = 'auth.login.rate_limited' AND [ScopeKind] = 'Platform' AND [ActorType] = 'Anonymous' AND [RateLimitPermitLimit] IS NOT NULL AND [RateLimitPermitLimit] > 0 AND [RateLimitWindowSeconds] IS NOT NULL AND [RateLimitWindowSeconds] > 0 AND [AggregationWindowStartUtc] IS NOT NULL AND [AggregationWindowEndUtc] IS NOT NULL AND [AggregationWindowStartUtc] < [AggregationWindowEndUtc]) OR ([EventType] <> 'auth.login.rate_limited' AND [RateLimitPermitLimit] IS NULL AND [RateLimitWindowSeconds] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: an auth.login.rate_limited row would violate the narrowed event type check and lose its
            // limiter columns, and security events are append-only. Down refuses while such a row exists; an empty
            // store (or one without the type) rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [security].[SecurityEvents] WHERE [EventType] = N'auth.login.rate_limited' OR [RateLimitPermitLimit] IS NOT NULL OR [RateLimitWindowSeconds] IS NOT NULL)
                BEGIN
                    THROW 51591, 'auth.login.rate_limited security events exist; AddLoginRateLimitedSecurityEvent is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecurityEvents_RateLimitSummary",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropColumn(
                name: "RateLimitPermitLimit",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.DropColumn(
                name: "RateLimitWindowSeconds",
                schema: "security",
                table: "SecurityEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecurityEvents_EventType",
                schema: "security",
                table: "SecurityEvents",
                sql: "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read')");
        }
    }
}
