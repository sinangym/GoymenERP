using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineCogsCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceLineCogsProposals",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedLineVersion = table.Column<long>(type: "bigint", nullable: false),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLineCogsProposals", x => new { x.CompanyId, x.Id });
                    table.UniqueConstraint("AK_InvoiceLineCogsProposals_CompanyId_InvoiceId_InvoiceLineId_Id", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.Id });
                    table.CheckConstraint("CK_InvoiceLineCogsProposals_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000' AND [PreparedBy] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_InvoiceLineCogsProposals_Json", "ISJSON([SourceJson]) = 1 AND ISJSON([ViewJson]) = 1");
                    table.CheckConstraint("CK_InvoiceLineCogsProposals_Utc", "DATEPART(TZOFFSET, [PreparedAtUtc]) = 0");
                    table.CheckConstraint("CK_InvoiceLineCogsProposals_Version", "[ExpectedLineVersion] >= 0");
                    table.ForeignKey(
                        name: "FK_InvoiceLineCogsProposals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLineCogsRevisions",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RevisionJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLineCogsRevisions", x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.Version });
                    table.CheckConstraint("CK_InvoiceLineCogsRevisions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceLineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_InvoiceLineCogsRevisions_Json", "ISJSON([ViewJson]) = 1 AND ISJSON([RevisionJson]) = 1");
                    table.CheckConstraint("CK_InvoiceLineCogsRevisions_MakerChecker", "[PreparedBy] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' AND [PreparedBy] <> [ApprovedBy]");
                    table.CheckConstraint("CK_InvoiceLineCogsRevisions_Utc", "DATEPART(TZOFFSET, [ApprovedAtUtc]) = 0");
                    table.CheckConstraint("CK_InvoiceLineCogsRevisions_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_InvoiceLineCogsRevisions_InvoiceLineCogsProposals_CompanyId_InvoiceId_InvoiceLineId_ProposalId",
                        columns: x => new { x.CompanyId, x.InvoiceId, x.InvoiceLineId, x.ProposalId },
                        principalSchema: "accounting",
                        principalTable: "InvoiceLineCogsProposals",
                        principalColumns: new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineCogsProposals_CompanyId_InvoiceId_InvoiceLineId_ExpectedLineVersion",
                schema: "accounting",
                table: "InvoiceLineCogsProposals",
                columns: new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "ExpectedLineVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineCogsRevisions_CompanyId_InvoiceId_InvoiceLineId_ProposalId",
                schema: "accounting",
                table: "InvoiceLineCogsRevisions",
                columns: new[] { "CompanyId", "InvoiceId", "InvoiceLineId", "ProposalId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineCogsRevisions_CompanyId_ProposalId",
                schema: "accounting",
                table: "InvoiceLineCogsRevisions",
                columns: new[] { "CompanyId", "ProposalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[InvoiceLineCogsProposals])
                    OR EXISTS (SELECT 1 FROM [accounting].[InvoiceLineCogsRevisions])
                BEGIN
                    THROW 51987, 'Invoice line COGS correction history exists; AddInvoiceLineCogsCorrections is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "InvoiceLineCogsRevisions",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "InvoiceLineCogsProposals",
                schema: "accounting");
        }
    }
}
