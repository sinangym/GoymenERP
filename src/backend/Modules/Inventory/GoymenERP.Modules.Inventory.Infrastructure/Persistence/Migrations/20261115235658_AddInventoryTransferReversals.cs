using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTransferReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferReversals",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferReversals", x => new { x.CompanyId, x.TransferId });
                    table.UniqueConstraint("AK_TransferReversals_CompanyId_TransferId_OriginalTransferId", x => new { x.CompanyId, x.TransferId, x.OriginalTransferId });
                    table.CheckConstraint("CK_TransferReversals_Identity", "[OriginalTransferId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalTransferId] <> [TransferId]");
                    table.CheckConstraint("CK_TransferReversals_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_TransferReversals_Transfers_CompanyId_OriginalTransferId",
                        columns: x => new { x.CompanyId, x.OriginalTransferId },
                        principalSchema: "inventory",
                        principalTable: "Transfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferReversals_Transfers_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "inventory",
                        principalTable: "Transfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferReversalLines",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferReversalLines", x => new { x.CompanyId, x.TransferId, x.LineId });
                    table.CheckConstraint("CK_TransferReversalLines_Identity", "[OriginalLineId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalTransferId] <> [TransferId]");
                    table.ForeignKey(
                        name: "FK_TransferReversalLines_TransferAcceptances_CompanyId_OriginalTransferId_OriginalLineId",
                        columns: x => new { x.CompanyId, x.OriginalTransferId, x.OriginalLineId },
                        principalSchema: "inventory",
                        principalTable: "TransferAcceptances",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferReversalLines_TransferLines_CompanyId_TransferId_LineId",
                        columns: x => new { x.CompanyId, x.TransferId, x.LineId },
                        principalSchema: "inventory",
                        principalTable: "TransferLines",
                        principalColumns: new[] { "CompanyId", "TransferId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferReversalLines_TransferReversals_CompanyId_TransferId_OriginalTransferId",
                        columns: x => new { x.CompanyId, x.TransferId, x.OriginalTransferId },
                        principalSchema: "inventory",
                        principalTable: "TransferReversals",
                        principalColumns: new[] { "CompanyId", "TransferId", "OriginalTransferId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferReversalLines_CompanyId_OriginalTransferId_OriginalLineId",
                schema: "inventory",
                table: "TransferReversalLines",
                columns: new[] { "CompanyId", "OriginalTransferId", "OriginalLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferReversalLines_CompanyId_TransferId_OriginalLineId",
                schema: "inventory",
                table: "TransferReversalLines",
                columns: new[] { "CompanyId", "TransferId", "OriginalLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferReversalLines_CompanyId_TransferId_OriginalTransferId",
                schema: "inventory",
                table: "TransferReversalLines",
                columns: new[] { "CompanyId", "TransferId", "OriginalTransferId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferReversals_CompanyId_OriginalTransferId",
                schema: "inventory",
                table: "TransferReversals",
                columns: new[] { "CompanyId", "OriginalTransferId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a reverse-transfer link is the KLN-D22 record of why a transfer exists; never drop it silently.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[TransferReversals])
                BEGIN
                    THROW 51722, 'Transfer reversal links exist; AddInventoryTransferReversals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "TransferReversalLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "TransferReversals",
                schema: "inventory");
        }
    }
}
