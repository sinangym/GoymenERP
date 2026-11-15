using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialInvoiceIdentityClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfficialInvoiceIdentityClaims",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentityKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    IdentityValue = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    HolderBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HolderSourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceIdentityClaims", x => x.Id);
                    table.CheckConstraint("CK_OfficialInvoiceIdentityClaims_Content", "[Generation] >= 1 AND LEN([ClaimedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialInvoiceIdentityClaims_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [HolderBatchId] <> '00000000-0000-0000-0000-000000000000' AND [HolderSourceDocumentId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OfficialInvoiceIdentityClaims_Value", "([IdentityKind] = 'official-no' AND LEN([IdentityValue]) > 0 AND DATALENGTH([IdentityValue]) = DATALENGTH(LTRIM(RTRIM([IdentityValue])))) OR ([IdentityKind] = 'ettn' AND LEN([IdentityValue]) = 36 AND DATALENGTH([IdentityValue]) = 72 AND [IdentityValue] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f-]%')");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceIdentityClaims_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceIdentityClaims_OfficialInvoiceImportBatches_CompanyId_HolderBatchId",
                        columns: x => new { x.CompanyId, x.HolderBatchId },
                        principalSchema: "finance",
                        principalTable: "OfficialInvoiceImportBatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceIdentityClaims_Holder",
                schema: "finance",
                table: "OfficialInvoiceIdentityClaims",
                columns: new[] { "CompanyId", "HolderBatchId", "HolderSourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "UX_OfficialInvoiceIdentityClaims_Generation",
                schema: "finance",
                table: "OfficialInvoiceIdentityClaims",
                columns: new[] { "CompanyId", "IdentityKind", "IdentityValue", "Generation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (refusal 51372): dropping the table would lose the FNS-D19 official number and ETTN claims
            // that keep a second open receivable from being admitted for the same official invoice. An empty store
            // rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceIdentityClaims])
                BEGIN
                    THROW 51372, 'Finance official invoice identity claims exist; AddOfficialInvoiceIdentityClaims is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OfficialInvoiceIdentityClaims",
                schema: "finance");
        }
    }
}
