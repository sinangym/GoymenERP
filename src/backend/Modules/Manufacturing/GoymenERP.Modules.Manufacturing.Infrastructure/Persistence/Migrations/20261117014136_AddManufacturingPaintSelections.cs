using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPaintSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_ProcessSpecProcess",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.AddColumn<string>(
                name: "PaintSelectionJson",
                schema: "manufacturing",
                table: "Plans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaintSelectionJson",
                schema: "manufacturing",
                table: "PlanRevisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_PaintSelection",
                schema: "manufacturing",
                table: "Plans",
                sql: "[PaintSelectionJson] IS NULL OR ISJSON([PaintSelectionJson]) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_ProcessSpecProcess",
                schema: "manufacturing",
                table: "Plans",
                sql: "([PackageCount] IS NULL AND [PiecesPerPackage] IS NULL AND [PieceLengthMm] IS NULL OR [Process] IN (3, 4)) AND ([UnitKg] IS NULL OR [Process] = 3) AND ([PerimeterMm] IS NULL AND [PaintDescription] IS NULL AND [PaintColorCode] IS NULL AND [PaintSelectionJson] IS NULL OR [Process] = 4) AND ([TargetThicknessMm] IS NULL OR [Process] = 2) AND ([Process] <> 4 OR [PaintDescription] IS NOT NULL OR [PaintSelectionJson] IS NOT NULL OR ([PackageCount] IS NULL AND [PiecesPerPackage] IS NULL AND [PieceLengthMm] IS NULL AND [PerimeterMm] IS NULL AND [PaintColorCode] IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlanRevisions_PaintSelection",
                schema: "manufacturing",
                table: "PlanRevisions",
                sql: "[PaintSelectionJson] IS NULL OR ISJSON([PaintSelectionJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[Plans] WHERE [PaintSelectionJson] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [manufacturing].[PlanRevisions] WHERE [PaintSelectionJson] IS NOT NULL)
                BEGIN
                    THROW 51976, 'Stored paint owner references would be lost; AddManufacturingPaintSelections is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_PaintSelection",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_ProcessSpecProcess",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlanRevisions_PaintSelection",
                schema: "manufacturing",
                table: "PlanRevisions");

            migrationBuilder.DropColumn(
                name: "PaintSelectionJson",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "PaintSelectionJson",
                schema: "manufacturing",
                table: "PlanRevisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_ProcessSpecProcess",
                schema: "manufacturing",
                table: "Plans",
                sql: "([PackageCount] IS NULL AND [PiecesPerPackage] IS NULL AND [PieceLengthMm] IS NULL OR [Process] IN (3, 4)) AND ([UnitKg] IS NULL OR [Process] = 3) AND ([PerimeterMm] IS NULL AND [PaintDescription] IS NULL AND [PaintColorCode] IS NULL OR [Process] = 4) AND ([TargetThicknessMm] IS NULL OR [Process] = 2) AND ([Process] <> 4 OR [PaintDescription] IS NOT NULL OR ([PackageCount] IS NULL AND [PiecesPerPackage] IS NULL AND [PieceLengthMm] IS NULL AND [PerimeterMm] IS NULL AND [PaintColorCode] IS NULL))");
        }
    }
}
