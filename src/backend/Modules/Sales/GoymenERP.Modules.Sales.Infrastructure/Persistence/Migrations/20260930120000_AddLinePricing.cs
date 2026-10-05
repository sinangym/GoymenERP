using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// AT-018 / AT-021 (user decision 2026-09-30): the order line's price-list REFERENCE and its visible adjustments (a discount
/// amount beside K3's discount percent, and the maturity surcharge as a percent or an amount). ONE nullable JSON column on
/// each of <c>sales.DraftLines</c> and the append-only archive <c>sales.DraftRevisionLines</c>, beside the three commercial
/// columns: null for every existing row (a line priced before this slice has no reference), present only beside a price
/// (<c>CK_*_Pricing</c>). The net is not stored: the calculator derives it. No row is rewritten.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260930120000_AddLinePricing")]
public sealed class AddLinePricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PricingJson",
            schema: "sales",
            table: "DraftLines",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PricingJson",
            schema: "sales",
            table: "DraftRevisionLines",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftLines_Pricing",
            schema: "sales",
            table: "DraftLines",
            sql: "[PricingJson] IS NULL OR ([UnitPrice] IS NOT NULL AND ISJSON([PricingJson]) = 1)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftRevisionLines_Pricing",
            schema: "sales",
            table: "DraftRevisionLines",
            sql: "[PricingJson] IS NULL OR ([UnitPrice] IS NOT NULL AND ISJSON([PricingJson]) = 1)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A recorded reference or adjustment is part of the line's price (and of an approved revision's archive); dropping
        // the columns would silently change what a line was priced at. While any exists the rollback is refused, before
        // any destructive operation, with this slice's own error (51064 — 51041–51059 and 51063 are taken on main or a
        // remote branch at the time of writing); an empty column rolls back freely. Forward fix, the 51057 pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftLines] WHERE [PricingJson] IS NOT NULL)
                OR EXISTS (SELECT 1 FROM [sales].[DraftRevisionLines] WHERE [PricingJson] IS NOT NULL)
                THROW 51064, 'Sales line pricing exists; AddLinePricing is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropCheckConstraint(name: "CK_DraftRevisionLines_Pricing", schema: "sales", table: "DraftRevisionLines");
        migrationBuilder.DropCheckConstraint(name: "CK_DraftLines_Pricing", schema: "sales", table: "DraftLines");
        migrationBuilder.DropColumn(name: "PricingJson", schema: "sales", table: "DraftRevisionLines");
        migrationBuilder.DropColumn(name: "PricingJson", schema: "sales", table: "DraftLines");
    }
}
