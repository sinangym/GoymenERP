using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EntityVersionBefore = table.Column<long>(type: "bigint", nullable: true),
                    EntityVersionAfter = table.Column<long>(type: "bigint", nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RoleCodesJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApprovalContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AiInvolvement = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AiContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceChannel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ClientIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    IntegrityHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_AfterJson_Valid", "ISJSON([AfterJson]) = 1");
                    table.CheckConstraint("CK_AuditEvents_AiJson_Valid", "[AiContextJson] IS NULL OR ISJSON([AiContextJson]) = 1");
                    table.CheckConstraint("CK_AuditEvents_ApprovalJson_Valid", "[ApprovalContextJson] IS NULL OR ISJSON([ApprovalContextJson]) = 1");
                    table.CheckConstraint("CK_AuditEvents_BeforeJson_Valid", "ISJSON([BeforeJson]) = 1");
                    table.CheckConstraint("CK_AuditEvents_CompanyId_NotEmpty", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AuditEvents_RoleCodesJson_Valid", "ISJSON([RoleCodesJson]) = 1");
                    table.CheckConstraint("CK_AuditEvents_SchemaVersion_Positive", "[SchemaVersion] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CompanyId_CorrelationId",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "CompanyId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CompanyId_EntityType_EntityId_OccurredAtUtc",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "CompanyId", "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CompanyId_OccurredAtUtc_Id",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "CompanyId", "OccurredAtUtc", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [audit].[TR_AuditEvents_RejectUpdateDelete]
                ON [audit].[AuditEvents]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Audit events are append-only and cannot be changed or deleted.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "audit");
        }
    }
}
