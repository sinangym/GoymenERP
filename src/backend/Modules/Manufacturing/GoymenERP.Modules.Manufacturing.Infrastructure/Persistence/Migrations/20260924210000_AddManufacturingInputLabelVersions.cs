using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persists the Inventory label version already validated when a plan is saved and copies it into
/// the production-order input snapshot. Existing rows stay null and therefore cannot silently join
/// a physical Inventory realization without an explicit forward fix.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260924210000_AddManufacturingInputLabelVersions")]
public sealed class AddManufacturingInputLabelVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ExpectedLabelVersion",
            schema: "manufacturing",
            table: "PlanInputs",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "ExpectedLabelVersion",
            schema: "manufacturing",
            table: "OrderInputs",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_PlanInputs_LabelVersion",
            schema: "manufacturing",
            table: "PlanInputs",
            sql: "[ExpectedLabelVersion] IS NULL OR [ExpectedLabelVersion] >= 1");

        migrationBuilder.AddCheckConstraint(
            name: "CK_OrderInputs_LabelVersion",
            schema: "manufacturing",
            table: "OrderInputs",
            sql: "[ExpectedLabelVersion] IS NULL OR [ExpectedLabelVersion] >= 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [manufacturing].[PlanInputs]
                WHERE [ExpectedLabelVersion] IS NOT NULL
            ) OR EXISTS (
                SELECT 1 FROM [manufacturing].[OrderInputs]
                WHERE [ExpectedLabelVersion] IS NOT NULL
            )
                THROW 51047, 'Manufacturing input label versions must be forward-fixed before downgrade.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_OrderInputs_LabelVersion",
            schema: "manufacturing",
            table: "OrderInputs");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PlanInputs_LabelVersion",
            schema: "manufacturing",
            table: "PlanInputs");

        migrationBuilder.DropColumn(
            name: "ExpectedLabelVersion",
            schema: "manufacturing",
            table: "OrderInputs");

        migrationBuilder.DropColumn(
            name: "ExpectedLabelVersion",
            schema: "manufacturing",
            table: "PlanInputs");
    }
}
