using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds nullable-on-legacy, all-or-none customer/tolling commercial provenance to plan creation,
/// every append-only revision and the order's immutable input snapshot. Down uses Manufacturing's
/// established 51047 refusal and never drops a populated commercial-history column.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20261004100000_AddManufacturingCommercialContexts")]
public sealed class AddManufacturingCommercialContexts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddColumns("PlanInputs", "CK_PlanInputs_CommercialContext");
        AddColumns("PlanRevisionInputs", "CK_PlanRevisionInputs_CommercialContext");
        AddColumns("OrderInputs", "CK_OrderInputs_CommercialContext");

        void AddColumns(string table, string constraint)
        {
            migrationBuilder.AddColumn<Guid>(name: "CommercialPartyId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "CommercialCompanyPartyAccountId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "CommercialPartyVersion", schema: "manufacturing", table: table,
                type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>(name: "CommercialAccountVersion", schema: "manufacturing", table: table,
                type: "nvarchar(max)", nullable: true);
            migrationBuilder.AddCheckConstraint(name: constraint, schema: "manufacturing", table: table,
                sql: CommercialContextCheck);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [manufacturing].[PlanInputs]
                WHERE [CommercialPartyId] IS NOT NULL OR [CommercialCompanyPartyAccountId] IS NOT NULL
                    OR [CommercialPartyVersion] IS NOT NULL OR [CommercialAccountVersion] IS NOT NULL)
                OR EXISTS (
                SELECT 1 FROM [manufacturing].[PlanRevisionInputs]
                WHERE [CommercialPartyId] IS NOT NULL OR [CommercialCompanyPartyAccountId] IS NOT NULL
                    OR [CommercialPartyVersion] IS NOT NULL OR [CommercialAccountVersion] IS NOT NULL)
                OR EXISTS (
                SELECT 1 FROM [manufacturing].[OrderInputs]
                WHERE [CommercialPartyId] IS NOT NULL OR [CommercialCompanyPartyAccountId] IS NOT NULL
                    OR [CommercialPartyVersion] IS NOT NULL OR [CommercialAccountVersion] IS NOT NULL)
                THROW 51047, 'Manufacturing commercial context history must be forward-fixed before downgrade.', 1;
            """);

        DropColumns("OrderInputs", "CK_OrderInputs_CommercialContext");
        DropColumns("PlanRevisionInputs", "CK_PlanRevisionInputs_CommercialContext");
        DropColumns("PlanInputs", "CK_PlanInputs_CommercialContext");

        void DropColumns(string table, string constraint)
        {
            migrationBuilder.DropCheckConstraint(name: constraint, schema: "manufacturing", table: table);
            migrationBuilder.DropColumn(name: "CommercialAccountVersion", schema: "manufacturing", table: table);
            migrationBuilder.DropColumn(name: "CommercialPartyVersion", schema: "manufacturing", table: table);
            migrationBuilder.DropColumn(name: "CommercialCompanyPartyAccountId", schema: "manufacturing", table: table);
            migrationBuilder.DropColumn(name: "CommercialPartyId", schema: "manufacturing", table: table);
        }
    }

    private const string CommercialContextCheck =
        "([CommercialPartyId] IS NULL AND [CommercialCompanyPartyAccountId] IS NULL "
        + "AND [CommercialPartyVersion] IS NULL AND [CommercialAccountVersion] IS NULL) OR "
        + "([CommercialPartyId] IS NOT NULL "
        + "AND [CommercialPartyId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [CommercialCompanyPartyAccountId] IS NOT NULL "
        + "AND [CommercialCompanyPartyAccountId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [CommercialPartyVersion] IS NOT NULL AND DATALENGTH([CommercialPartyVersion]) > 0 "
        + "AND [CommercialAccountVersion] IS NOT NULL AND DATALENGTH([CommercialAccountVersion]) > 0)";
}
