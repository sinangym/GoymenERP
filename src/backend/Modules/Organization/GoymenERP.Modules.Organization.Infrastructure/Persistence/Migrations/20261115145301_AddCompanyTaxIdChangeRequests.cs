using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyTaxIdChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KIM-S23 (KIM-D28, DEC-ORG-005): the schema is not tightened over data that already breaks the rule. A real
            // company without a VKN or two companies sharing one is refused here; the data is corrected by a human
            // (no placeholder VKN is invented and no company is chosen), then the migration is applied again.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [organization].[Companies] WHERE [IsPlaceholder] = 0 AND [Vkn] IS NULL) BEGIN THROW 51441, 'A non-placeholder company has no VKN; AddCompanyTaxIdChangeRequests is not applied until it is corrected.', 1; END;""");

            migrationBuilder.Sql("""IF EXISTS (SELECT [Vkn] FROM [organization].[Companies] WHERE [Vkn] IS NOT NULL GROUP BY [Vkn] HAVING COUNT(*) > 1) BEGIN THROW 51442, 'Two companies share a VKN; AddCompanyTaxIdChangeRequests is not applied until it is corrected.', 1; END;""");

            migrationBuilder.CreateTable(
                name: "CompanyTaxIdChangeRequests",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentVkn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ProposedVkn = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CompanyVersionAtRequest = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyTaxIdChangeRequests", x => x.Id);
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_CurrentVkn", "[CurrentVkn] IS NULL OR (LEN([CurrentVkn]) = 10 AND [CurrentVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_Decision", "([Status] = N'Pending' AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionNote] IS NULL) OR ([Status] <> N'Pending' AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_DistinctVkn", "[CurrentVkn] IS NULL OR [CurrentVkn] <> [ProposedVkn]");
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_MakerChecker", "[DecidedBy] IS NULL OR [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [RequestedBy] COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_ProposedVkn", "LEN([ProposedVkn]) = 10 AND [ProposedVkn] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'");
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_Status", "[Status] IN (N'Pending', N'Approved', N'Rejected')");
                    table.CheckConstraint("CK_CompanyTaxIdChangeRequests_Version", "[Version] >= 1 AND [CompanyVersionAtRequest] >= 1");
                    table.ForeignKey(
                        name: "FK_CompanyTaxIdChangeRequests_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Vkn",
                schema: "organization",
                table: "Companies",
                column: "Vkn",
                unique: true,
                filter: "[Vkn] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Companies_Vkn_RequiredUnlessPlaceholder",
                schema: "organization",
                table: "Companies",
                sql: "[IsPlaceholder] = 1 OR [Vkn] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyTaxIdChangeRequests_CompanyId_Pending",
                schema: "organization",
                table: "CompanyTaxIdChangeRequests",
                column: "CompanyId",
                unique: true,
                filter: "[Status] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyTaxIdChangeRequests_CompanyId_RequestedAtUtc",
                schema: "organization",
                table: "CompanyTaxIdChangeRequests",
                columns: new[] { "CompanyId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [organization].[CompanyTaxIdChangeRequests]) BEGIN THROW 51443, 'Company VKN change requests exist; AddCompanyTaxIdChangeRequests is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "CompanyTaxIdChangeRequests",
                schema: "organization");

            migrationBuilder.DropIndex(
                name: "IX_Companies_Vkn",
                schema: "organization",
                table: "Companies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Companies_Vkn_RequiredUnlessPlaceholder",
                schema: "organization",
                table: "Companies");
        }
    }
}
