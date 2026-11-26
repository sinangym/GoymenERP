using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCommandLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "CommandRecords",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BindingSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    RecordJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandRecords", x => new { x.CompanyId, x.PreviewId });
                    table.CheckConstraint("CK_AiCommandRecords_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PreviewId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND [State] BETWEEN 1 AND 9 AND [Version] > 0 AND ISJSON([RecordJson]) = 1 AND DATALENGTH([BindingSha256]) = 64 AND [BindingSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.ForeignKey(
                        name: "FK_CommandRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommandEvents",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToolId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ToolVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreviewHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: true),
                    ApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ResultVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandEvents", x => new { x.CompanyId, x.EventId });
                    table.CheckConstraint("CK_AiCommandEvents_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND [Kind] BETWEEN 1 AND 10 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0 AND ([PreviewId] IS NULL OR [PreviewId] <> '00000000-0000-0000-0000-000000000000') AND ([PreviewHash] IS NULL OR (DATALENGTH([PreviewHash]) = 64 AND [PreviewHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'))");
                    table.ForeignKey(
                        name: "FK_CommandEvents_CommandRecords_CompanyId_PreviewId",
                        columns: x => new { x.CompanyId, x.PreviewId },
                        principalSchema: "ai",
                        principalTable: "CommandRecords",
                        principalColumns: new[] { "CompanyId", "PreviewId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommandEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandEvents_CompanyId_PreviewId_OccurredAtUtc_EventId",
                schema: "ai",
                table: "CommandEvents",
                columns: new[] { "CompanyId", "PreviewId", "OccurredAtUtc", "EventId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ai].[CommandRecords] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [ai].[CommandEvents] WITH (UPDLOCK, HOLDLOCK))
                BEGIN
                    THROW 52200, 'AI command state or audit exists; AddAiCommandLedger is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "CommandEvents",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "CommandRecords",
                schema: "ai");
        }
    }
}
