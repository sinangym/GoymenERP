using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAiKnowledgeApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "aiknowledge");

            migrationBuilder.CreateTable(
                name: "CommandAttempts",
                schema: "aiknowledge",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKeySha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    Command = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    RequestSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RefusalCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandAttempts", x => new { x.CompanyId, x.ActorUserId, x.IdempotencyKeySha256 });
                    table.CheckConstraint("CK_AiKnowledgeCommandAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorUserId] <> '00000000-0000-0000-0000-000000000000' AND [TargetId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Command]) > 0 AND LEN([CorrelationId]) > 0 AND DATALENGTH([IdempotencyKeySha256]) = 64 AND [IdempotencyKeySha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND DATALENGTH([RequestSha256]) = 64 AND [RequestSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND (([Status] IN (200, 201) AND [RefusalCode] IS NULL) OR ([Status] = 409 AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0))");
                    table.ForeignKey(
                        name: "FK_CommandAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackageApprovals",
                schema: "aiknowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<int>(type: "int", nullable: false),
                    PackageSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnOriginCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageApprovals", x => x.Id);
                    table.CheckConstraint("CK_AiKnowledgePackageApprovals_Identity", "[OriginCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceKind] IN (1, 2) AND LEN([CorrelationId]) > 0 AND DATALENGTH([PackageSha256]) = 64 AND [PackageSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_AiKnowledgePackageApprovals_State", "([Status] = 1 AND [Version] = 1 AND [WithdrawnByUserId] IS NULL AND [WithdrawnOriginCompanyId] IS NULL AND [WithdrawnAtUtc] IS NULL AND [WithdrawReason] IS NULL) OR ([Status] = 2 AND [Version] = 2 AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnByUserId] <> '00000000-0000-0000-0000-000000000000' AND [WithdrawnOriginCompanyId] IS NOT NULL AND [WithdrawnOriginCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnAtUtc] >= [ApprovedAtUtc] AND [WithdrawReason] IS NOT NULL AND LEN([WithdrawReason]) > 0)");
                    table.ForeignKey(
                        name: "FK_PackageApprovals_Companies_OriginCompanyId",
                        column: x => x.OriginCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageApprovals_Companies_WithdrawnOriginCompanyId",
                        column: x => x.WithdrawnOriginCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageApprovals_OriginCompanyId",
                schema: "aiknowledge",
                table: "PackageApprovals",
                column: "OriginCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageApprovals_SourceKind_ApprovedAtUtc_Id",
                schema: "aiknowledge",
                table: "PackageApprovals",
                columns: new[] { "SourceKind", "ApprovedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageApprovals_WithdrawnOriginCompanyId",
                schema: "aiknowledge",
                table: "PackageApprovals",
                column: "WithdrawnOriginCompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_AiKnowledgePackageApprovals_Active",
                schema: "aiknowledge",
                table: "PackageApprovals",
                columns: new[] { "SourceKind", "PackageSha256" },
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: an approval or an attempt is the record of a human decision; it is never dropped by a rollback.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [aiknowledge].[PackageApprovals]) OR EXISTS (SELECT 1 FROM [aiknowledge].[CommandAttempts])
                BEGIN
                    THROW 52290, 'AI knowledge package approvals exist; InitialAiKnowledgeApprovals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CommandAttempts",
                schema: "aiknowledge");

            migrationBuilder.DropTable(
                name: "PackageApprovals",
                schema: "aiknowledge");
        }
    }
}
