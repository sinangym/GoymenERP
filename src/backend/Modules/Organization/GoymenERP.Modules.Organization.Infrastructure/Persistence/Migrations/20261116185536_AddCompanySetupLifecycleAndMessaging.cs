using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySetupLifecycleAndMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LifecycleState",
                schema: "organization",
                table: "Companies",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<Guid>(
                name: "OpenedByUserId",
                schema: "organization",
                table: "Companies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanySetupInboxReceipts",
                schema: "organization",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySetupInboxReceipts", x => new { x.CompanyId, x.EventId });
                    table.CheckConstraint("CK_CompanySetupInbox_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND LEN([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%'");
                });

            migrationBuilder.CreateTable(
                name: "CompanySetupOutboxMessages",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Position = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySetupOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_CompanySetupOutbox_Payload", "ISJSON([PayloadJson]) = 1");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Companies_Lifecycle",
                schema: "organization",
                table: "Companies",
                sql: "[LifecycleState] IN (N'Active', N'SetupPending') AND ([OpenedByUserId] IS NULL OR [OpenedByUserId] <> '00000000-0000-0000-0000-000000000000') AND ([LifecycleState] <> N'SetupPending' OR [OpenedByUserId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySetupOutboxMessages_CompanyId_Position",
                schema: "organization",
                table: "CompanySetupOutboxMessages",
                columns: new[] { "CompanyId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[CompanySetupInboxReceipts])
                    OR EXISTS (SELECT 1 FROM [organization].[CompanySetupOutboxMessages])
                    OR EXISTS (SELECT 1 FROM [organization].[Companies] WHERE [LifecycleState] <> N'Active' OR [OpenedByUserId] IS NOT NULL)
                BEGIN
                    THROW 51945, 'Company setup lifecycle or history exists; AddCompanySetupLifecycleAndMessaging is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CompanySetupInboxReceipts",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "CompanySetupOutboxMessages",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Companies_Lifecycle",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LifecycleState",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OpenedByUserId",
                schema: "organization",
                table: "Companies");
        }
    }
}
