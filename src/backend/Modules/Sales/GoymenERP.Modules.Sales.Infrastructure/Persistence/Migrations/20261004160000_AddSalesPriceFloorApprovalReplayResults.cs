using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// PL04: the price-floor approval attempt keeps its first complete response in visible and withheld forms.
/// The fresh price-read fence chooses between them on replay; no later draft mutation can rewrite the first result.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20261004160000_AddSalesPriceFloorApprovalReplayResults")]
public sealed class AddSalesPriceFloorApprovalReplayResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ResultJson",
            schema: "sales",
            table: "DraftAttempts",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftAttempts_Result",
            schema: "sales",
            table: "DraftAttempts",
            sql: SalesDbContext.AttemptResultConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftAttempts] WHERE [ResultJson] IS NOT NULL)
                THROW 51064, 'Sales price-floor replay results exist; AddSalesPriceFloorApprovalReplayResults is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropCheckConstraint(name: "CK_DraftAttempts_Result", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropColumn(name: "ResultJson", schema: "sales", table: "DraftAttempts");
    }
}
