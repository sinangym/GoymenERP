using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// DEC-CAR-003 / PRD-PERM-001 (#99): Product card deactivation (product.card.deactivate.v1) and
    /// reactivation (product.card.reactivate.v1) get replay terminals. Only the command-name check widens;
    /// no row is written or changed.
    /// Down follows this module's existing rule (InitialGroupSharedTerminals): committed Product replay
    /// records are retained, so the rollback is refused while any exists; forward-fix.
    /// </summary>
    public partial class AllowProductLifecycleTerminals : Migration
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
                sql: "[Subject] = 'Product' AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1', 'product.card.activate.v1', 'product.card.deactivate.v1', 'product.card.reactivate.v1')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals])
                    THROW 51043, 'Committed Product replay records must be retained; use a forward fix.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_GroupSharedTerminals_ProductCommands",
                schema: "idempotency",
                table: "GroupSharedCommandTerminals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GroupSharedTerminals_ProductCommands",
                schema: "idempotency",
                table: "GroupSharedCommandTerminals",
                sql: "[Subject] = 'Product' AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1', 'product.card.activate.v1')");
        }
    }
}
