using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkAssistantMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantMemoryEntries",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CloseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMemoryEntries", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkAssistantMemoryEntries_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [OwnerUserId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1, 2) AND LEN([Body]) > 0 AND [Version] >= 1 AND LEN([CorrelationId]) > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc] AND [ExpiresAtUtc] > [UpdatedAtUtc]");
                    table.CheckConstraint("CK_WorkAssistantMemoryEntries_State", "([State] = 1 AND [EndedAtUtc] IS NULL AND [EndedByUserId] IS NULL AND [CloseReason] IS NULL) OR ([State] = 2 AND [EndedAtUtc] IS NOT NULL AND [EndedByUserId] = [OwnerUserId] AND [CloseReason] IS NULL AND [Version] >= 2) OR ([State] = 3 AND [EndedAtUtc] IS NOT NULL AND [EndedByUserId] IS NOT NULL AND [EndedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [CloseReason] IS NOT NULL AND LEN([CloseReason]) > 0 AND [Version] >= 2)");
                    table.ForeignKey(
                        name: "FK_AssistantMemoryEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssistantMemoryOversightViews",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMemoryOversightViews", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkAssistantMemoryOversightViews_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ViewerUserId] <> '00000000-0000-0000-0000-000000000000' AND [SubjectUserId] <> '00000000-0000-0000-0000-000000000000' AND [EntryCount] >= 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_AssistantMemoryOversightViews_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssistantMemoryUserClosures",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMemoryUserClosures", x => new { x.CompanyId, x.SubjectUserId });
                    table.CheckConstraint("CK_WorkAssistantMemoryUserClosures_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SubjectUserId] <> '00000000-0000-0000-0000-000000000000' AND [ChangedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_AssistantMemoryUserClosures_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMemoryEntries_CompanyId_OwnerUserId_State_UpdatedAtUtc_Id",
                schema: "work",
                table: "AssistantMemoryEntries",
                columns: new[] { "CompanyId", "OwnerUserId", "State", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMemoryOversightViews_CompanyId_SubjectUserId_ViewedAtUtc_Id",
                schema: "work",
                table: "AssistantMemoryOversightViews",
                columns: new[] { "CompanyId", "SubjectUserId", "ViewedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP-C6 forward fix (AI-D30): memory entries are personal data whose retention/destruction is a separate, undecided
            // policy; closures and overseer view rows are evidence of who looked at whose memory. Dropping the tables with any
            // row present would destroy them outside that policy, so Down refuses; an empty store still rolls back.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[AssistantMemoryEntries])
                    OR EXISTS (SELECT 1 FROM [work].[AssistantMemoryUserClosures])
                    OR EXISTS (SELECT 1 FROM [work].[AssistantMemoryOversightViews])
                    THROW 52210, 'Work assistant memory entries, closures or oversight views exist; AddWorkAssistantMemory is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "AssistantMemoryEntries",
                schema: "work");

            migrationBuilder.DropTable(
                name: "AssistantMemoryOversightViews",
                schema: "work");

            migrationBuilder.DropTable(
                name: "AssistantMemoryUserClosures",
                schema: "work");
        }
    }
}
