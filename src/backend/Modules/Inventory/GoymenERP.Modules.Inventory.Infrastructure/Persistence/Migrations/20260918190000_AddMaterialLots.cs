using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// T07 lot record (DEC-PRD-003, Ö3): the minimal <c>Lots</c> table, and every label bound to a lot
/// of its own company by (company, lot) against the lot's (company, id) alternate key.
/// No lot is written here. A label row whose lot does not exist makes this migration fail when the
/// foreign key is added: an orphan label is reported, never repaired with an invented lot. Labels
/// are written only by the store and the SQL tests today (there is no write route), so no deployed
/// table is expected to hold one.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260918190000_AddMaterialLots")]
public sealed class AddMaterialLots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Lots",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HeatNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CoilNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Lots", x => x.Id);
                table.UniqueConstraint("AK_Lots_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Lots_CreatedBy", "LEN([CreatedBy]) > 0");
                table.CheckConstraint("CK_Lots_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Lots_Trace",
                    "([HeatNumber] IS NULL OR LEN([HeatNumber]) > 0) AND ([CoilNumber] IS NULL OR LEN([CoilNumber]) > 0)");
                table.CheckConstraint("CK_Lots_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_Lots_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Lots_CompanyId_ProductId",
            schema: "inventory",
            table: "Lots",
            columns: new[] { "CompanyId", "ProductId" });

        migrationBuilder.CreateIndex(
            name: "IX_Labels_CompanyId_LotId",
            schema: "inventory",
            table: "Labels",
            columns: new[] { "CompanyId", "LotId" });

        migrationBuilder.AddForeignKey(
            name: "FK_Labels_Lots_CompanyId_LotId",
            schema: "inventory",
            table: "Labels",
            columns: new[] { "CompanyId", "LotId" },
            principalSchema: "inventory",
            principalTable: "Lots",
            principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Labels_Lots_CompanyId_LotId",
            schema: "inventory",
            table: "Labels");

        migrationBuilder.DropIndex(
            name: "IX_Labels_CompanyId_LotId",
            schema: "inventory",
            table: "Labels");

        migrationBuilder.DropTable(
            name: "Lots",
            schema: "inventory");
    }
}
