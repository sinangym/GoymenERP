using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferDispatchDocumentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentVersion",
                schema: "inventory",
                table: "TransferDispatches",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferDispatches_DocumentVersion",
                schema: "inventory",
                table: "TransferDispatches",
                sql: "[DocumentVersion] IS NULL OR [DocumentVersion] >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[TransferDispatches] WHERE [DocumentVersion] IS NOT NULL)
                BEGIN
                    THROW 51272, 'Inventory transfer dispatches proved against a Logistics outgoing document version exist; AddInventoryTransferDispatchDocumentVersion is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferDispatches_DocumentVersion",
                schema: "inventory",
                table: "TransferDispatches");

            migrationBuilder.DropColumn(
                name: "DocumentVersion",
                schema: "inventory",
                table: "TransferDispatches");
        }
    }
}
