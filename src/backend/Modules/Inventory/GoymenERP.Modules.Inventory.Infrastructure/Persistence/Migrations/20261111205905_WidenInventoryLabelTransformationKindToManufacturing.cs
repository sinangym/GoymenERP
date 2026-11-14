using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenInventoryLabelTransformationKindToManufacturing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LabelTransformations_Kind",
                schema: "inventory",
                table: "LabelTransformations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LabelTransformations_Kind",
                schema: "inventory",
                table: "LabelTransformations",
                sql: "[Kind] IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[LabelTransformations] WHERE [Kind] = 3)
                BEGIN
                    THROW 51262, 'Inventory manufacturing label genealogy rows exist; WidenInventoryLabelTransformationKindToManufacturing is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_LabelTransformations_Kind",
                schema: "inventory",
                table: "LabelTransformations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LabelTransformations_Kind",
                schema: "inventory",
                table: "LabelTransformations",
                sql: "[Kind] IN (1, 2)");
        }
    }
}
