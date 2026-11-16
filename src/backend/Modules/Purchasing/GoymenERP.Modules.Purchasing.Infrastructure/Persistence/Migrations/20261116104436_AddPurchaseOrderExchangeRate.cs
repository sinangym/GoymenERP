using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderExchangeRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseOrderExchangeRateChanges",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false),
                    TargetCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RateDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    BulletinDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderExchangeRateChanges", x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter });
                    table.CheckConstraint("CK_PurchaseOrderExchangeRateChanges_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [TargetCurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseOrderExchangeRateChanges_Kind", "[Kind] IN (1)");
                    table.CheckConstraint("CK_PurchaseOrderExchangeRateChanges_Rate", "[Rate] > 0");
                    table.CheckConstraint("CK_PurchaseOrderExchangeRateChanges_Source", "([Source] = 1 AND [BulletinDate] IS NOT NULL AND [Reason] IS NULL) OR ([Source] = 2 AND [BulletinDate] IS NULL)");
                    table.CheckConstraint("CK_PurchaseOrderExchangeRateChanges_Text", "([Reason] IS NULL OR LEN([Reason]) > 0) AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseOrderExchangeRateChanges_Version", "[OrderVersionAfter] >= 1");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderExchangeRateChanges_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ALC-SC33 forward fix: a recorded order rate (FNS-D79/D80/D91) is history; Down refuses while any exists.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderExchangeRateChanges]) BEGIN THROW 51833, 'Purchase order exchange rates exist; AddPurchaseOrderExchangeRate is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "PurchaseOrderExchangeRateChanges",
                schema: "purchasing");
        }
    }
}
