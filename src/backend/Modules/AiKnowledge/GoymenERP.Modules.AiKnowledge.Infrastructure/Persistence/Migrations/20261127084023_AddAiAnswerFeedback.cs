using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnswerFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackEvents",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PriorVersion = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetainUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackEvents", x => new { x.CompanyId, x.AnswerId, x.Version });
                    table.CheckConstraint("CK_AiFeedbackEvents_Event", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AnswerId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND (([Version] = 1 AND [PriorVersion] IS NULL) OR ([Version] > 1 AND [PriorVersion] IS NOT NULL AND [PriorVersion] = [Version] - 1)) AND [Kind] BETWEEN 1 AND 4 AND ([Kind] <> 4 OR [PriorVersion] IS NOT NULL) AND (([Kind] = 1 AND [ReasonCode] IS NULL) OR ([ReasonCode] IS NOT NULL AND DATALENGTH([ReasonCode]) = DATALENGTH(RTRIM([ReasonCode])) AND [ReasonCode] COLLATE Latin1_General_100_BIN2 IN ('incorrect_information','incorrect_or_missing_source','incomplete_answer','outdated_information','other'))) AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[RetainUntilUtc]) = 0 AND [RetainUntilUtc] = DATEADD(day,90,[CreatedAtUtc])");
                    table.ForeignKey(
                        name: "FK_FeedbackEvents_AnswerReceipts_CompanyId_AnswerId",
                        columns: x => new { x.CompanyId, x.AnswerId },
                        principalSchema: "ai",
                        principalTable: "AnswerReceipts",
                        principalColumns: new[] { "CompanyId", "AnswerId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackEvents_FeedbackEvents_CompanyId_AnswerId_PriorVersion",
                        columns: x => new { x.CompanyId, x.AnswerId, x.PriorVersion },
                        principalSchema: "ai",
                        principalTable: "FeedbackEvents",
                        principalColumns: new[] { "CompanyId", "AnswerId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackIdentities",
                schema: "ai",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKeySha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    RequestSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackIdentities", x => new { x.CompanyId, x.AnswerId, x.Version });
                    table.CheckConstraint("CK_AiFeedbackIdentities_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AnswerId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [ActorId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0 AND DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.ForeignKey(
                        name: "FK_FeedbackIdentities_FeedbackEvents_CompanyId_AnswerId_Version",
                        columns: x => new { x.CompanyId, x.AnswerId, x.Version },
                        principalSchema: "ai",
                        principalTable: "FeedbackEvents",
                        principalColumns: new[] { "CompanyId", "AnswerId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackEvents_CompanyId_AnswerId_PriorVersion",
                schema: "ai",
                table: "FeedbackEvents",
                columns: new[] { "CompanyId", "AnswerId", "PriorVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackEvents_CompanyId_CreatedAtUtc_AnswerId_Version",
                schema: "ai",
                table: "FeedbackEvents",
                columns: new[] { "CompanyId", "CreatedAtUtc", "AnswerId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackEvents_CompanyId_RetainUntilUtc",
                schema: "ai",
                table: "FeedbackEvents",
                columns: new[] { "CompanyId", "RetainUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackIdentities_CompanyId_ActorId_IdempotencyKeySha256",
                schema: "ai",
                table: "FeedbackIdentities",
                columns: new[] { "CompanyId", "ActorId", "IdempotencyKeySha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [ai].[FeedbackEvents] WITH (UPDLOCK, HOLDLOCK))
                    OR EXISTS (SELECT 1 FROM [ai].[FeedbackIdentities] WITH (UPDLOCK, HOLDLOCK))
                BEGIN
                    THROW 52313, 'AI feedback history or separated identity exists; AddAiAnswerFeedback is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "FeedbackIdentities",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "FeedbackEvents",
                schema: "ai");
        }
    }
}
