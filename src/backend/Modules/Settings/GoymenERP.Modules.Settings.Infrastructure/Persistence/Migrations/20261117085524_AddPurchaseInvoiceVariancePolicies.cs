using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceVariancePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceVariancePolicies",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountTolerance = table.Column<decimal>(type: "decimal(24,6)", precision: 24, scale: 6, nullable: false),
                    RateTolerance = table.Column<decimal>(type: "decimal(24,6)", precision: 24, scale: 6, nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceVariancePolicies", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_PurchaseInvoiceVariancePolicies_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_PurchaseInvoiceVariancePolicies_Publication", "LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND DATEPART(TZOFFSET,[PublishedAtUtc]) = 0");
                    table.CheckConstraint("CK_PurchaseInvoiceVariancePolicies_Values", "[AmountTolerance] >= 0 AND [RateTolerance] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceVariancePolicies_CompanyId_PolicyId",
                schema: "settings",
                table: "PurchaseInvoiceVariancePolicies",
                columns: new[] { "CompanyId", "PolicyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[PurchaseInvoiceVariancePolicies])
                BEGIN
                    THROW 51985, 'Published purchase invoice variance history exists; AddPurchaseInvoiceVariancePolicies is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceVariancePolicies",
                schema: "settings");
        }
    }
}
