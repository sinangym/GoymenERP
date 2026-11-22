using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkApprovalInboxEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalInboxEvents",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClientEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalInboxEvents", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkApprovalInboxEvents_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ItemId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND [EventType] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_WorkApprovalInboxEvents_Shape", "([EventType] IN (1, 4) AND [ClientEventId] IS NULL) OR ([EventType] IN (2, 3) AND [ClientEventId] IS NOT NULL AND [ClientEventId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_WorkApprovalInboxEvents_Time", "([EventType] <> 1 OR [OccurredAtUtc] = [CycleStartedAtUtc]) AND [RecordedAtUtc] >= [CycleStartedAtUtc]");
                    table.ForeignKey(
                        name: "FK_ApprovalInboxEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_WorkApprovalInboxEvents_ClientEvent",
                schema: "work",
                table: "ApprovalInboxEvents",
                columns: new[] { "CompanyId", "ClientEventId" },
                unique: true,
                filter: "[ClientEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_WorkApprovalInboxEvents_Cycle",
                schema: "work",
                table: "ApprovalInboxEvents",
                columns: new[] { "CompanyId", "Kind", "ItemId", "CycleStartedAtUtc", "EventType" },
                unique: true,
                filter: "[EventType] IN (1, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // W1-A forward fix: measurement events are append-only evidence of inbox waits; an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[ApprovalInboxEvents])
                BEGIN
                    THROW 52080, 'Work approval inbox measurement events exist; AddWorkApprovalInboxEvents is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "ApprovalInboxEvents",
                schema: "work");
        }
    }
}
