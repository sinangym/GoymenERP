using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkApprovalRuleVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalRuleVersions",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DurationClass = table.Column<int>(type: "int", nullable: false),
                    ApproverPermission = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EscalationPermission = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRuleVersions", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkApprovalRuleVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecordedByUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND [Version] >= 1 AND [DurationClass] IN (1, 2) AND LEN([ApproverPermission]) > 0 AND ([EscalationPermission] IS NULL OR LEN([EscalationPermission]) > 0) AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_ApprovalRuleVersions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_WorkApprovalRuleVersions_KindVersion",
                schema: "work",
                table: "ApprovalRuleVersions",
                columns: new[] { "CompanyId", "Kind", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP1 forward fix: a recorded rule version is the company's approval rule decision (who changed which kind's class
            // or escalation, when and why); dropping it would silently fall back to the defaults. An empty table rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[ApprovalRuleVersions])
                BEGIN
                    THROW 52100, 'Work approval rule versions exist; AddWorkApprovalRuleVersions is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "ApprovalRuleVersions",
                schema: "work");
        }
    }
}
