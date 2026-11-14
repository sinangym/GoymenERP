using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.SecurityEvents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSecurityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "security");

            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                schema: "security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ScopeKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    EventType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Phase = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Severity = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PermissionCode = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: true),
                    PolicyId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    PolicyVersion = table.Column<int>(type: "int", nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    SourceChannel = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AiToolId = table.Column<string>(type: "varchar(96)", unicode: false, maxLength: 96, nullable: true),
                    AiToolVersion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    AiRiskClass = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    AiDataClass = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    RetainUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionPolicyRef = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    LegalHold = table.Column<bool>(type: "bit", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    AggregationWindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AggregationWindowEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IntegrityHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.Id);
                    table.CheckConstraint("CK_SecurityEvents_AggregationWindow", "([AggregationWindowStartUtc] IS NULL AND [AggregationWindowEndUtc] IS NULL AND [OccurrenceCount] = 1) OR ([AggregationWindowStartUtc] IS NOT NULL AND [AggregationWindowEndUtc] IS NOT NULL AND [AggregationWindowStartUtc] <= [AggregationWindowEndUtc])");
                    table.CheckConstraint("CK_SecurityEvents_EventType", "[EventType] IN ('auth.login.succeeded', 'auth.login.failed', 'authz.denied', 'ai.authz.denied', 'identity.permission.changed', 'settings.policy.published', 'export.performed', 'retention.purge.requested', 'ai.tool.called', 'ai.request.refused', 'security.events.read')");
                    table.CheckConstraint("CK_SecurityEvents_LegalHold", "[LegalHold] = 0");
                    table.CheckConstraint("CK_SecurityEvents_OccurrenceCount", "[OccurrenceCount] >= 1");
                    table.CheckConstraint("CK_SecurityEvents_Outcome", "[Outcome] IN ('Allowed', 'Denied', 'Failed', 'Unknown')");
                    table.CheckConstraint("CK_SecurityEvents_Phase", "[Phase] IN ('Single', 'Started', 'Completed')");
                    table.CheckConstraint("CK_SecurityEvents_PhaseEventType", "([EventType] = 'ai.tool.called' AND [Phase] IN ('Started', 'Completed')) OR ([EventType] <> 'ai.tool.called' AND [Phase] = 'Single')");
                    table.CheckConstraint("CK_SecurityEvents_RetainUntil", "[RetainUntilUtc] > [OccurredAtUtc]");
                    table.CheckConstraint("CK_SecurityEvents_ScopeCompany", "([ScopeKind] = 'Company' AND [CompanyId] IS NOT NULL) OR ([ScopeKind] = 'Platform' AND [CompanyId] IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_CompanyId_OccurredAtUtc",
                schema: "security",
                table: "SecurityEvents",
                columns: new[] { "CompanyId", "OccurredAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_CorrelationId",
                schema: "security",
                table: "SecurityEvents",
                column: "CorrelationId");

            // Insert-only channel (ADR-SEC-001 §1): v1 has no trigger exception and no purge path.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [security].[TR_SecurityEvents_RejectUpdateDelete]
                ON [security].[SecurityEvents]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51271, 'Security events are append-only and cannot be changed or deleted.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-fix guard: refuse to drop a non-empty security event channel. An empty table rolls back.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [security].[SecurityEvents])
                BEGIN
                    THROW 51270, 'Security events exist; InitialSecurityEvents is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "SecurityEvents",
                schema: "security");
        }
    }
}
