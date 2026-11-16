using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferLossRequestNumbers : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// KLN-D97: loss requests and their decisions are numbered per transfer line. Every stored request and decision
        /// is the line's first one (1); one approval per line stays enforced by a filtered unique index.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransferLossDecisions_TransferLossRequests_CompanyId_TransferId_LineId",
                schema: "inventory",
                table: "TransferLossDecisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransferLossRequests",
                schema: "inventory",
                table: "TransferLossRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransferLossDecisions",
                schema: "inventory",
                table: "TransferLossDecisions");

            migrationBuilder.AddColumn<int>(
                name: "RequestNumber",
                schema: "inventory",
                table: "TransferLossRequests",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RequestNumber",
                schema: "inventory",
                table: "TransferLossDecisions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransferLossRequests",
                schema: "inventory",
                table: "TransferLossRequests",
                columns: new[] { "CompanyId", "TransferId", "LineId", "RequestNumber" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransferLossDecisions",
                schema: "inventory",
                table: "TransferLossDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId", "RequestNumber" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferLossRequests_RequestNumber",
                schema: "inventory",
                table: "TransferLossRequests",
                sql: "[RequestNumber] >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_TransferLossDecisions_CompanyId_TransferId_LineId",
                schema: "inventory",
                table: "TransferLossDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId" },
                unique: true,
                filter: "[Decision] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_TransferLossDecisions_TransferLossRequests_CompanyId_TransferId_LineId_RequestNumber",
                schema: "inventory",
                table: "TransferLossDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId", "RequestNumber" },
                principalSchema: "inventory",
                principalTable: "TransferLossRequests",
                principalColumns: new[] { "CompanyId", "TransferId", "LineId", "RequestNumber" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Forward fix: a renewed loss request (number above 1) or its decision would collide on the per-line key, so the
        /// rollback refuses while one exists. An empty store, or one holding only first requests, rolls back freely.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[TransferLossRequests] WHERE [RequestNumber] > 1)
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferLossDecisions] WHERE [RequestNumber] > 1)
                BEGIN
                    THROW 51811, 'Renewed transfer loss requests exist; AddInventoryTransferLossRequestNumbers is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_TransferLossDecisions_TransferLossRequests_CompanyId_TransferId_LineId_RequestNumber",
                schema: "inventory",
                table: "TransferLossDecisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransferLossRequests",
                schema: "inventory",
                table: "TransferLossRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferLossRequests_RequestNumber",
                schema: "inventory",
                table: "TransferLossRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransferLossDecisions",
                schema: "inventory",
                table: "TransferLossDecisions");

            migrationBuilder.DropIndex(
                name: "IX_TransferLossDecisions_CompanyId_TransferId_LineId",
                schema: "inventory",
                table: "TransferLossDecisions");

            migrationBuilder.DropColumn(
                name: "RequestNumber",
                schema: "inventory",
                table: "TransferLossRequests");

            migrationBuilder.DropColumn(
                name: "RequestNumber",
                schema: "inventory",
                table: "TransferLossDecisions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransferLossRequests",
                schema: "inventory",
                table: "TransferLossRequests",
                columns: new[] { "CompanyId", "TransferId", "LineId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransferLossDecisions",
                schema: "inventory",
                table: "TransferLossDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId" });

            migrationBuilder.AddForeignKey(
                name: "FK_TransferLossDecisions_TransferLossRequests_CompanyId_TransferId_LineId",
                schema: "inventory",
                table: "TransferLossDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId" },
                principalSchema: "inventory",
                principalTable: "TransferLossRequests",
                principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
