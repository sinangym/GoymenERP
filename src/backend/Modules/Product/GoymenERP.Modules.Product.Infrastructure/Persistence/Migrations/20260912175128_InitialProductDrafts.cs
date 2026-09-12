using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProductDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "product");

            migrationBuilder.CreateTable(
                name: "CodeCounters",
                schema: "product",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeCounters", x => x.Prefix);
                    table.CheckConstraint("CK_CodeCounters_Sequence", "[LastSequence] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CodeGroups",
                schema: "product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Turkish_100_CI_AS"),
                    Prefix = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeGroups", x => x.Id);
                    table.CheckConstraint("CK_CodeGroups_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(52)", unicode: false, maxLength: 52, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false, collation: "Turkish_100_CI_AS"),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchasableValue = table.Column<bool>(type: "bit", nullable: true),
                    PurchasableProvenance = table.Column<int>(type: "int", nullable: false),
                    SellableValue = table.Column<bool>(type: "bit", nullable: true),
                    SellableProvenance = table.Column<int>(type: "int", nullable: false),
                    ProductionInputValue = table.Column<bool>(type: "bit", nullable: true),
                    ProductionInputProvenance = table.Column<int>(type: "int", nullable: false),
                    ProductionOutputValue = table.Column<bool>(type: "bit", nullable: true),
                    ProductionOutputProvenance = table.Column<int>(type: "int", nullable: false),
                    Lifecycle = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReferencesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [GroupId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Products_LifecycleVersion", "[Lifecycle] IN (0,1,2) AND [Version] >= 1");
                    table.CheckConstraint("CK_Products_OptionalReferences", "([ClassId] IS NULL OR [ClassId] <> '00000000-0000-0000-0000-000000000000') AND ([FamilyId] IS NULL OR [FamilyId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_Products_ProductionInput", "([ProductionInputProvenance] = 0 AND [ProductionInputValue] IS NULL) OR ([ProductionInputProvenance] IN (1,2) AND [ProductionInputValue] IS NOT NULL)");
                    table.CheckConstraint("CK_Products_ProductionOutput", "([ProductionOutputProvenance] = 0 AND [ProductionOutputValue] IS NULL) OR ([ProductionOutputProvenance] IN (1,2) AND [ProductionOutputValue] IS NOT NULL)");
                    table.CheckConstraint("CK_Products_Purchasable", "([PurchasableProvenance] = 0 AND [PurchasableValue] IS NULL) OR ([PurchasableProvenance] IN (1,2) AND [PurchasableValue] IS NOT NULL)");
                    table.CheckConstraint("CK_Products_ReferencesJson", "ISJSON([ReferencesJson], OBJECT) = 1");
                    table.CheckConstraint("CK_Products_Sellable", "([SellableProvenance] = 0 AND [SellableValue] IS NULL) OR ([SellableProvenance] IN (1,2) AND [SellableValue] IS NOT NULL)");
                    table.CheckConstraint("CK_Products_Timestamps", "[UpdatedAtUtc] >= [CreatedAtUtc] AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_Products_CodeGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "product",
                        principalTable: "CodeGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CodeRegistrations",
                schema: "product",
                columns: table => new
                {
                    Code = table.Column<string>(type: "varchar(52)", unicode: false, maxLength: 52, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeRegistrations", x => x.Code);
                    table.ForeignKey(
                        name: "FK_CodeRegistrations_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodeRegistrations_ProductId",
                schema: "product",
                table: "CodeRegistrations",
                column: "ProductId",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Code",
                schema: "product",
                table: "Products",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_GroupId_Id",
                schema: "product",
                table: "Products",
                columns: new[] { "GroupId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name_Id",
                schema: "product",
                table: "Products",
                columns: new[] { "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_UpdatedAtUtc_Id",
                schema: "product",
                table: "Products",
                columns: new[] { "UpdatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[Products])
                    OR EXISTS (SELECT 1 FROM [product].[CodeRegistrations])
                    OR EXISTS (SELECT 1 FROM [product].[CodeCounters])
                    OR EXISTS (SELECT 1 FROM [product].[CodeGroups])
                    THROW 51044, 'Product identity, code reservations and reference data require a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "CodeCounters",
                schema: "product");

            migrationBuilder.DropTable(
                name: "CodeRegistrations",
                schema: "product");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "product");

            migrationBuilder.DropTable(
                name: "CodeGroups",
                schema: "product");
        }
    }
}
