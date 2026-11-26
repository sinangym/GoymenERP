using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyWideLoadingShareIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LOJ-D21: stop before schema mutation; historical movement identities must never be rewritten automatically.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [logistics].[LoadingShares]
                    GROUP BY [CompanyId], [ShareId]
                    HAVING COUNT_BIG(*) > 1
                )
                    THROW 52350, 'Company-wide share identity collisions exist; inspect the history before applying this migration. No rows were changed.', 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_LoadingShares_CompanyId_ShareId",
                schema: "logistics",
                table: "LoadingShares",
                columns: new[] { "CompanyId", "ShareId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[LoadingShares])
                    THROW 52351, 'Share identity history exists; AddCompanyWideLoadingShareIdentity is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropIndex(
                name: "UX_LoadingShares_CompanyId_ShareId",
                schema: "logistics",
                table: "LoadingShares");
        }
    }
}
