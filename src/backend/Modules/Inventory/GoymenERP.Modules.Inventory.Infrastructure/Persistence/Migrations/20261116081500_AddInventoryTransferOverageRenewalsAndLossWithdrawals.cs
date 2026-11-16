using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferOverageRenewalsAndLossWithdrawals : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// KLN-D107: an excess decision names the request it decides. Every stored decision decides the receipt itself
        /// (request 1); corrected re-requests number from 2 and one approval per line stays enforced by a filtered unique
        /// index. KLN-D108: a withdrawal closes exactly one undecided numbered loss request.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_TransferOverageDecisions",
                schema: "inventory",
                table: "TransferOverageDecisions");

            migrationBuilder.AddColumn<int>(
                name: "RequestNumber",
                schema: "inventory",
                table: "TransferOverageDecisions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransferOverageDecisions",
                schema: "inventory",
                table: "TransferOverageDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId", "RequestNumber" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOverageDecisions_RequestNumber",
                schema: "inventory",
                table: "TransferOverageDecisions",
                sql: "[RequestNumber] >= 1");

            migrationBuilder.CreateTable(
                name: "TransferLossWithdrawals",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferLossWithdrawals", x => new { x.CompanyId, x.TransferId, x.LineId, x.RequestNumber });
                    table.CheckConstraint("CK_TransferLossWithdrawals_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([WithdrawnBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferLossWithdrawals_TransferLossRequests_CompanyId_TransferId_LineId_RequestNumber",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId, x.RequestNumber },
                        principalSchema: "inventory",
                        principalTable: "TransferLossRequests",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId", "RequestNumber" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferOverageRenewals",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestNumber = table.Column<int>(type: "int", nullable: false),
                    OverageKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOverageRenewals", x => new { x.CompanyId, x.TransferId, x.LineId, x.RequestNumber });
                    table.CheckConstraint("CK_TransferOverageRenewals_Quantity", "[OverageKg] > 0");
                    table.CheckConstraint("CK_TransferOverageRenewals_RequestNumber", "[RequestNumber] >= 2");
                    table.CheckConstraint("CK_TransferOverageRenewals_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferOverageRenewals_TransferReceipts_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferReceipts",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOverageDecisions_CompanyId_TransferId_LineId",
                schema: "inventory",
                table: "TransferOverageDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId" },
                unique: true,
                filter: "[Decision] = 1");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Forward fix: a corrected excess re-request, a decision of one (number above 1) or a loss withdrawal would be
        /// lost or collide on the per-line key, so the rollback refuses while one exists. An empty store, or one holding
        /// only first-request decisions, rolls back freely.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[TransferOverageRenewals])
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferOverageDecisions] WHERE [RequestNumber] > 1)
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferLossWithdrawals])
                BEGIN
                    THROW 51836, 'Transfer excess re-requests or loss withdrawals exist; AddInventoryTransferOverageRenewalsAndLossWithdrawals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "TransferLossWithdrawals",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferOverageRenewals",
                schema: "inventory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TransferOverageDecisions",
                schema: "inventory",
                table: "TransferOverageDecisions");

            migrationBuilder.DropIndex(
                name: "IX_TransferOverageDecisions_CompanyId_TransferId_LineId",
                schema: "inventory",
                table: "TransferOverageDecisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOverageDecisions_RequestNumber",
                schema: "inventory",
                table: "TransferOverageDecisions");

            migrationBuilder.DropColumn(
                name: "RequestNumber",
                schema: "inventory",
                table: "TransferOverageDecisions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TransferOverageDecisions",
                schema: "inventory",
                table: "TransferOverageDecisions",
                columns: new[] { "CompanyId", "TransferId", "LineId" });
        }
    }
}
