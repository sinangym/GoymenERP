using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseAcceptanceParticipant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseAcceptanceShares",
                schema: "purchasing",
                columns: table => new
                {
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseAcceptanceShares", x => x.AcceptanceId);
                    table.UniqueConstraint("AK_PurchaseAcceptanceShares_CompanyId_AcceptanceId", x => new { x.CompanyId, x.AcceptanceId });
                    table.CheckConstraint("CK_PurchaseAcceptanceShares_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_PurchaseAcceptanceShares_Identity", "[AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseAcceptanceShares_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_PurchaseAcceptanceShares_Text", "LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
                    table.CheckConstraint("CK_PurchaseAcceptanceShares_Version", "[OrderVersionAfter] >= 1");
                    table.ForeignKey(
                        name: "FK_PurchaseAcceptanceShares_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseAcceptanceShares_OrderLineRecorded",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares",
                columns: new[] { "CompanyId", "OrderId", "LineId", "RecordedAtUtc", "AcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseAcceptanceShares_Idempotency",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares",
                columns: new[] { "CompanyId", "IdempotencyKeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseAcceptanceShares",
                schema: "purchasing");
        }
    }
}
