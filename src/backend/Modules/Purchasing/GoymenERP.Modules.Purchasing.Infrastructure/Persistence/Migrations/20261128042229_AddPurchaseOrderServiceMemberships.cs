using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderServiceMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseOrderServiceMemberships",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedVersion = table.Column<long>(type: "bigint", nullable: false),
                    SupplierPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    MemberNo = table.Column<int>(type: "int", nullable: false),
                    MemberCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LineIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderServiceMemberships", x => new { x.CompanyId, x.ServiceOrderId });
                    table.CheckConstraint("CK_PurchaseOrderServiceMemberships_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [ServiceOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SupplierPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [PreparedVersion] > 0 AND [OrderVersionAfter] > 1 AND [MemberNo] > 0 AND [MemberCount] >= [MemberNo]");
                    table.CheckConstraint("CK_PurchaseOrderServiceMemberships_Snapshot", "ISJSON([LineIdsJson]) = 1 AND LEFT(LTRIM([LineIdsJson]), 1) = N'[' AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderServiceMemberships_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderServiceMemberships_ServiceOrderRevisions_CompanyId_ServiceOrderId_PreparedVersion",
                        columns: x => new { x.CompanyId, x.ServiceOrderId, x.PreparedVersion },
                        principalSchema: "purchasing",
                        principalTable: "ServiceOrderRevisions",
                        principalColumns: new[] { "CompanyId", "OrderId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderServiceMemberships_CompanyId_OrderId_OrderVersionAfter_MemberNo",
                schema: "purchasing",
                table: "PurchaseOrderServiceMemberships",
                columns: new[] { "CompanyId", "OrderId", "OrderVersionAfter", "MemberNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderServiceMemberships_CompanyId_ServiceOrderId_PreparedVersion",
                schema: "purchasing",
                table: "PurchaseOrderServiceMemberships",
                columns: new[] { "CompanyId", "ServiceOrderId", "PreparedVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderServiceMemberships])
                BEGIN
                    THROW 52361, 'Source mixed order membership exists; AddPurchaseOrderServiceMemberships is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PurchaseOrderServiceMemberships",
                schema: "purchasing");
        }
    }
}
