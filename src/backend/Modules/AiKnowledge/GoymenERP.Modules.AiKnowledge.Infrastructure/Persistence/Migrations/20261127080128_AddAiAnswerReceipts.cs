using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnswerReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnswerReceipts",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    CitationsSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    Feature = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ModelDeployment = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetainUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerReceipts", x => new { x.CompanyId, x.AnswerId });
                    table.CheckConstraint("CK_AiAnswerReceipts_Evidence", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AnswerId] <> '00000000-0000-0000-0000-000000000000' AND (DATALENGTH([AnswerSha256]) = 64 AND [AnswerSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%') AND (DATALENGTH([CitationsSha256]) = 64 AND [CitationsSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%') AND [Feature] = 'Panel' AND LEN(LTRIM(RTRIM([ModelDeployment]))) > 0 AND LEN(LTRIM(RTRIM([PolicyVersion]))) > 0 AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[RetainUntilUtc]) = 0 AND [RetainUntilUtc] = DATEADD(day,90,[CreatedAtUtc])");
                    table.ForeignKey(
                        name: "FK_AnswerReceipts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnswerReceiptIdentities",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerReceiptIdentities", x => new { x.CompanyId, x.AnswerId });
                    table.CheckConstraint("CK_AiAnswerReceiptIdentities_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AnswerId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0");
                    table.ForeignKey(
                        name: "FK_AnswerReceiptIdentities_AnswerReceipts_CompanyId_AnswerId",
                        columns: x => new { x.CompanyId, x.AnswerId },
                        principalSchema: "ai",
                        principalTable: "AnswerReceipts",
                        principalColumns: new[] { "CompanyId", "AnswerId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerReceipts_CompanyId_RetainUntilUtc_AnswerId",
                schema: "ai",
                table: "AnswerReceipts",
                columns: new[] { "CompanyId", "RetainUntilUtc", "AnswerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ai].[AnswerReceipts] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [ai].[AnswerReceiptIdentities] WITH (UPDLOCK, HOLDLOCK))
                BEGIN
                    THROW 52312, 'AI answer evidence or separated identity exists; AddAiAnswerReceipts is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "AnswerReceiptIdentities",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "AnswerReceipts",
                schema: "ai");
        }
    }
}
