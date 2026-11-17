using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCogsClassificationTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GroupSharedTerminals_ProductCommands",
                schema: "idempotency",
                table: "GroupSharedCommandTerminals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GroupSharedTerminals_ProductCommands",
                schema: "idempotency",
                table: "GroupSharedCommandTerminals",
                sql: "[Subject] = 'Product' AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1', 'product.card.activate.v1', 'product.card.deactivate.v1', 'product.card.reactivate.v1', 'product.card.cogs-classification.set.v1') AND ([CommandName] <> 'product.card.cogs-classification.set.v1' OR DATALENGTH([CommandName]) = DATALENGTH('product.card.cogs-classification.set.v1'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals] WHERE [CommandName] = 'product.card.cogs-classification.set.v1')
                BEGIN
                    THROW 51981, 'Explicit COGS classification evidence exists; AddProductCogsClassificationTerminals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_GroupSharedTerminals_ProductCommands",
                schema: "idempotency",
                table: "GroupSharedCommandTerminals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GroupSharedTerminals_ProductCommands",
                schema: "idempotency",
                table: "GroupSharedCommandTerminals",
                sql: "[Subject] = 'Product' AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1', 'product.card.activate.v1', 'product.card.deactivate.v1', 'product.card.reactivate.v1')");
        }
    }
}
