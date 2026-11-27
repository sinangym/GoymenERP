using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindOfficialInvoiceClaimsToAdmissionReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [finance].[OfficialInvoiceIdentityClaims] AS c
                    WHERE NOT EXISTS (SELECT 1 FROM [finance].[OpenItemSourceBatches] AS r
                        WHERE r.[CompanyId] = c.[CompanyId] AND r.[Id] = c.[HolderBatchId]))
                BEGIN
                    THROW 51373, 'Official invoice identity claim has no Finance admission receipt; migration refused without backfill.', 1;
                END;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_OfficialInvoiceIdentityClaims_OfficialInvoiceImportBatches_CompanyId_HolderBatchId",
                schema: "finance",
                table: "OfficialInvoiceIdentityClaims");

            migrationBuilder.AddForeignKey(
                name: "FK_OfficialInvoiceIdentityClaims_OpenItemSourceBatches_CompanyId_HolderBatchId",
                schema: "finance",
                table: "OfficialInvoiceIdentityClaims",
                columns: new[] { "CompanyId", "HolderBatchId" },
                principalSchema: "finance",
                principalTable: "OpenItemSourceBatches",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [finance].[OfficialInvoiceIdentityClaims] AS c
                    WHERE NOT EXISTS (SELECT 1 FROM [finance].[OfficialInvoiceImportBatches] AS b
                        WHERE b.[CompanyId] = c.[CompanyId] AND b.[Id] = c.[HolderBatchId]))
                BEGIN
                    THROW 51374, 'Native Finance admission holds official invoice identity; BindOfficialInvoiceClaimsToAdmissionReceipts is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_OfficialInvoiceIdentityClaims_OpenItemSourceBatches_CompanyId_HolderBatchId",
                schema: "finance",
                table: "OfficialInvoiceIdentityClaims");

            migrationBuilder.AddForeignKey(
                name: "FK_OfficialInvoiceIdentityClaims_OfficialInvoiceImportBatches_CompanyId_HolderBatchId",
                schema: "finance",
                table: "OfficialInvoiceIdentityClaims",
                columns: new[] { "CompanyId", "HolderBatchId" },
                principalSchema: "finance",
                principalTable: "OfficialInvoiceImportBatches",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
