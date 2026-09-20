using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SalesDbContext))]
[Migration("20260920073846_AddDraftRevisions")]
public sealed class AddDraftRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DraftRevisions",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MoneyProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MoneyScale = table.Column<int>(type: "int", nullable: false),
                ArchivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ArchivedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftRevisions", x => new { x.CompanyId, x.DraftId, x.Revision });
                table.CheckConstraint("CK_DraftRevisions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND LEN([SubmittedBy]) > 0 AND LEN([ApprovedBy]) > 0 AND LEN([ArchivedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_DraftRevisions_Money", "[MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND LEN([MoneyProfileVersion]) > 0 AND [MoneyScale] >= 0");
                table.ForeignKey(
                    name: "FK_DraftRevisions_Drafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "sales",
                    principalTable: "Drafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DraftRevisionLines",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                QuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UnitPrice = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                PriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DiscountPercent = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftRevisionLines", x => new { x.CompanyId, x.DraftId, x.Revision, x.LineNo });
                table.CheckConstraint("CK_DraftRevisionLines_Identity", "[LineNo] >= 1 AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND LEN([QuantityText]) > 0");
                table.CheckConstraint("CK_DraftRevisionLines_Price", "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_DraftRevisionLines_DraftRevisions_CompanyId_DraftId_Revision",
                    columns: x => new { x.CompanyId, x.DraftId, x.Revision },
                    principalSchema: "sales",
                    principalTable: "DraftRevisions",
                    principalColumns: new[] { "CompanyId", "DraftId", "Revision" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_DraftRevisionLines_LineId",
            schema: "sales",
            table: "DraftRevisionLines",
            columns: new[] { "CompanyId", "DraftId", "Revision", "LineId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DraftRevisionLines",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "DraftRevisions",
            schema: "sales");
    }
}
