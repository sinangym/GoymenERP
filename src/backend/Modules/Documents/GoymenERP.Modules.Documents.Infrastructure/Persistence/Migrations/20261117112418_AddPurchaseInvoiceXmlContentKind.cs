using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceXmlContentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs",
                sql: "[ContentKind] IN (1, 2, 3, 4, 5, 6, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [documents].[ContentBlobs] WHERE [ContentKind] = 7)
                BEGIN
                    THROW 51995, 'UBL invoice source exists; AddPurchaseInvoiceXmlContentKind is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs",
                sql: "[ContentKind] IN (1, 2, 3, 4, 5, 6)");
        }
    }
}
