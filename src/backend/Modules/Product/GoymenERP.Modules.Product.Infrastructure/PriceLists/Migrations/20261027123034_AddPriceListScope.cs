using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// MOD-02 (SAT-D9): the list scope. Every existing list becomes company-wide (<c>ScopeKind</c> = 1, the column default);
    /// no row is otherwise rewritten. <c>Down</c> refuses (<c>THROW 51072</c>, used by no other migration on main or any remote
    /// branch at the time of writing) while any customer-specific list exists — dropping the columns would silently turn it
    /// into a company-wide price (forward fix); with company-wide lists only it rolls back freely.
    /// </summary>
    public partial class AddPriceListScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScopeCompanyAccountId",
                schema: "product",
                table: "PriceLists",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScopeKind",
                schema: "product",
                table: "PriceLists",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "ScopePartyId",
                schema: "product",
                table: "PriceLists",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_CompanyId_Scope",
                schema: "product",
                table: "PriceLists",
                columns: new[] { "CompanyId", "ScopeKind", "ScopeCompanyAccountId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PriceLists_Scope",
                schema: "product",
                table: "PriceLists",
                sql: "([ScopeKind] = 1 AND [ScopePartyId] IS NULL AND [ScopeCompanyAccountId] IS NULL) OR ([ScopeKind] = 3 AND [ScopePartyId] IS NOT NULL AND [ScopePartyId] <> '00000000-0000-0000-0000-000000000000' AND [ScopeCompanyAccountId] IS NOT NULL AND [ScopeCompanyAccountId] <> '00000000-0000-0000-0000-000000000000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[PriceLists] WHERE [ScopeKind] <> 1)
                    THROW 51072, 'Customer-specific price lists exist; AddPriceListScope is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_PriceLists_CompanyId_Scope",
                schema: "product",
                table: "PriceLists");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PriceLists_Scope",
                schema: "product",
                table: "PriceLists");

            migrationBuilder.DropColumn(
                name: "ScopeCompanyAccountId",
                schema: "product",
                table: "PriceLists");

            migrationBuilder.DropColumn(
                name: "ScopeKind",
                schema: "product",
                table: "PriceLists");

            migrationBuilder.DropColumn(
                name: "ScopePartyId",
                schema: "product",
                table: "PriceLists");
        }
    }
}
