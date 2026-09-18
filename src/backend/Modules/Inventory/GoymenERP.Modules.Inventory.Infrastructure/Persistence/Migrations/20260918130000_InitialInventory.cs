using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// Inventory's first schema (INV-2): labels with their prints and documents, movements with
/// their lines, and balances. Every table is a tenant table; locations and companies are
/// Organization's, referenced by key only, so this runs after Organization's migrations.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260918130000_InitialInventory")]
public sealed class InitialInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "inventory");

        migrationBuilder.CreateTable(
            name: "Labels",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HeatNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CoilNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                RequiredDocumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Labels", x => x.Id);
                table.UniqueConstraint("AK_Labels_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Labels_Code", "LEN([Code]) > 0");
                table.CheckConstraint("CK_Labels_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [LotId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Labels_RequiredDocumentsJson", "ISJSON([RequiredDocumentsJson]) = 1");
                table.CheckConstraint("CK_Labels_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_Labels_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Movements",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                SourceKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CostStatus = table.Column<int>(type: "int", nullable: false),
                CostAmount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                CostCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Movements", x => x.Id);
                table.UniqueConstraint("AK_Movements_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Movements_Cost",
                    "([CostStatus] IN (1, 2) AND [CostAmount] IS NULL AND [CostCurrencyId] IS NULL) OR "
                    + "([CostStatus] = 3 AND [CostAmount] IS NOT NULL AND [CostAmount] >= 0 AND [CostCurrencyId] IS NOT NULL "
                    + "AND [CostCurrencyId] <> '00000000-0000-0000-0000-000000000000')");
                table.CheckConstraint("CK_Movements_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND ([SourceLineId] IS NULL OR [SourceLineId] <> '00000000-0000-0000-0000-000000000000')");
                table.CheckConstraint("CK_Movements_Kind", "[Kind] BETWEEN 1 AND 4");
                table.CheckConstraint("CK_Movements_PayloadHash", "LEN([PayloadHash]) = 64");
                table.CheckConstraint("CK_Movements_SourceKind", "LEN([SourceKind]) > 0");
                table.ForeignKey(
                    name: "FK_Movements_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LabelDocuments",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                AttachedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                AttachedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LabelDocuments", x => new { x.CompanyId, x.LabelId, x.Sequence });
                table.CheckConstraint("CK_LabelDocuments_Sequence", "[Sequence] >= 1");
                table.CheckConstraint("CK_LabelDocuments_Text", "LEN([Kind]) > 0 AND LEN([AttachedBy]) > 0");
                table.ForeignKey(
                    name: "FK_LabelDocuments_Labels_CompanyId_LabelId",
                    columns: x => new { x.CompanyId, x.LabelId },
                    principalSchema: "inventory",
                    principalTable: "Labels",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LabelPrints",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                PrintedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                PrintedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LabelPrints", x => new { x.CompanyId, x.LabelId, x.Sequence });
                table.CheckConstraint("CK_LabelPrints_PrintedBy", "LEN([PrintedBy]) > 0");
                table.CheckConstraint("CK_LabelPrints_Reason",
                    "([Sequence] = 1 AND [Reason] IS NULL) OR ([Sequence] > 1 AND [Reason] IS NOT NULL AND LEN([Reason]) > 0)");
                table.CheckConstraint("CK_LabelPrints_Sequence", "[Sequence] >= 1");
                table.ForeignKey(
                    name: "FK_LabelPrints_Labels_CompanyId_LabelId",
                    columns: x => new { x.CompanyId, x.LabelId },
                    principalSchema: "inventory",
                    principalTable: "Labels",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Balances",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OwnerKind = table.Column<int>(type: "int", nullable: false),
                OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustodianKind = table.Column<int>(type: "int", nullable: false),
                CustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Block = table.Column<int>(type: "int", nullable: false),
                QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Balances", x => x.Id);
                table.CheckConstraint("CK_Balances_Enums", "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2)");
                table.CheckConstraint("CK_Balances_Quantity", "[QuantityKg] >= 0");
                table.ForeignKey(
                    name: "FK_Balances_Labels_CompanyId_LabelId",
                    columns: x => new { x.CompanyId, x.LabelId },
                    principalSchema: "inventory",
                    principalTable: "Labels",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Balances_Locations_CompanyId_LocationId",
                    columns: x => new { x.CompanyId, x.LocationId },
                    principalSchema: "organization",
                    principalTable: "Locations",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "MovementLines",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNumber = table.Column<int>(type: "int", nullable: false),
                Direction = table.Column<int>(type: "int", nullable: false),
                OwnerKind = table.Column<int>(type: "int", nullable: false),
                OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustodianKind = table.Column<int>(type: "int", nullable: false),
                CustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Block = table.Column<int>(type: "int", nullable: false),
                QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovementLines", x => new { x.CompanyId, x.MovementId, x.LineNumber });
                table.CheckConstraint("CK_MovementLines_Enums",
                    "[Direction] IN (1, 2) AND [OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2)");
                table.CheckConstraint("CK_MovementLines_LineNumber", "[LineNumber] >= 1");
                table.CheckConstraint("CK_MovementLines_Quantity", "[QuantityKg] > 0");
                table.ForeignKey(
                    name: "FK_MovementLines_Labels_CompanyId_LabelId",
                    columns: x => new { x.CompanyId, x.LabelId },
                    principalSchema: "inventory",
                    principalTable: "Labels",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MovementLines_Locations_CompanyId_LocationId",
                    columns: x => new { x.CompanyId, x.LocationId },
                    principalSchema: "organization",
                    principalTable: "Locations",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MovementLines_Movements_CompanyId_MovementId",
                    columns: x => new { x.CompanyId, x.MovementId },
                    principalSchema: "inventory",
                    principalTable: "Movements",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Balances_CompanyId_LocationId",
            schema: "inventory",
            table: "Balances",
            columns: new[] { "CompanyId", "LocationId" });

        migrationBuilder.CreateIndex(
            name: "UX_Balances_Position",
            schema: "inventory",
            table: "Balances",
            columns: new[] { "CompanyId", "LabelId", "OwnerKind", "OwnerId", "CustodianKind", "CustodianId", "LocationId", "ProductId", "Block" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Labels_CompanyId_Code",
            schema: "inventory",
            table: "Labels",
            columns: new[] { "CompanyId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Labels_CompanyId_ProductId",
            schema: "inventory",
            table: "Labels",
            columns: new[] { "CompanyId", "ProductId" });

        migrationBuilder.CreateIndex(
            name: "IX_MovementLines_CompanyId_LabelId",
            schema: "inventory",
            table: "MovementLines",
            columns: new[] { "CompanyId", "LabelId" });

        migrationBuilder.CreateIndex(
            name: "IX_MovementLines_CompanyId_LocationId",
            schema: "inventory",
            table: "MovementLines",
            columns: new[] { "CompanyId", "LocationId" });

        migrationBuilder.CreateIndex(
            name: "IX_Movements_CompanyId_RecordedAtUtc_Id",
            schema: "inventory",
            table: "Movements",
            columns: new[] { "CompanyId", "RecordedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "UX_Movements_Source",
            schema: "inventory",
            table: "Movements",
            columns: new[] { "CompanyId", "Kind", "SourceKind", "SourceId" },
            unique: true,
            filter: "[SourceLineId] IS NULL");

        migrationBuilder.CreateIndex(
            name: "UX_Movements_SourceLine",
            schema: "inventory",
            table: "Movements",
            columns: new[] { "CompanyId", "Kind", "SourceKind", "SourceId", "SourceLineId" },
            unique: true,
            filter: "[SourceLineId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Balances", schema: "inventory");
        migrationBuilder.DropTable(name: "LabelDocuments", schema: "inventory");
        migrationBuilder.DropTable(name: "LabelPrints", schema: "inventory");
        migrationBuilder.DropTable(name: "MovementLines", schema: "inventory");
        migrationBuilder.DropTable(name: "Labels", schema: "inventory");
        migrationBuilder.DropTable(name: "Movements", schema: "inventory");
    }
}
