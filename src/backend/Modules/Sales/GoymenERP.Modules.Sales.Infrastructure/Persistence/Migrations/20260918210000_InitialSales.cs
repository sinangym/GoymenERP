using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// Sales's first schema (SALES-P2, T03): the order-draft header, its lines and delivery slices with
/// the owners' snapshots, the R3 attempt record and the technical number counter. All are tenant
/// tables; companies are Organization's, referenced by key only, so this runs after Organization's
/// migrations. No approval, revision, shipment, invoice or receipt table exists here (T33 and the
/// other owners' records are not this slice).
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260918210000_InitialSales")]
public sealed class InitialSales : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "sales");

        migrationBuilder.CreateTable(
            name: "DraftNumbers",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Next = table.Column<long>(type: "bigint", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftNumbers", x => x.CompanyId);
                table.CheckConstraint("CK_DraftNumbers_Identity", "[CompanyId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_DraftNumbers_Next", "[Next] >= 1");
                table.CheckConstraint("CK_DraftNumbers_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_DraftNumbers_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Drafts",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                DeliveryAddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DeliveryAddressVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ContactVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ResponsibleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ResponsibleVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                PaymentTermsId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PaymentTermsVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                SourceStrategyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SourceStrategyVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PackageVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                MoneyOperation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MoneyProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MoneyEffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                MoneyIsActive = table.Column<bool>(type: "bit", nullable: false),
                MoneyEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                MoneyEffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                MoneyScale = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Drafts", x => x.Id);
                table.UniqueConstraint("AK_Drafts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Drafts_Identity",
                    "[Id] <> " + NotEmptyGuid + " AND [CompanyId] <> " + NotEmptyGuid + " "
                    + "AND [PartyId] <> " + NotEmptyGuid + " AND [CompanyAccountId] <> " + NotEmptyGuid + " "
                    + "AND [CurrencyId] <> " + NotEmptyGuid + " AND [MoneyProfileId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_Drafts_State", "[Kind] = 1 AND [State] = 1 AND [Revision] = 0");
                table.CheckConstraint("CK_Drafts_Version", "[Version] >= 1");
                table.CheckConstraint("CK_Drafts_Text",
                    "LEN([DocumentNo]) > 0 AND LEN([MoneyOperation]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_Drafts_Money",
                    "[MoneyScale] >= 0 AND ([MoneyEffectiveThrough] IS NULL OR [MoneyEffectiveThrough] >= [MoneyEffectiveFrom])");
                table.CheckConstraint("CK_Drafts_References",
                    "(([DeliveryAddressId] IS NULL AND [DeliveryAddressVersion] IS NULL) OR ([DeliveryAddressId] IS NOT NULL AND [DeliveryAddressVersion] IS NOT NULL)) AND (([ContactId] IS NULL AND [ContactVersion] IS NULL) OR ([ContactId] IS NOT NULL AND [ContactVersion] IS NOT NULL)) "
                    + "AND (([ResponsibleId] IS NULL AND [ResponsibleVersion] IS NULL) OR ([ResponsibleId] IS NOT NULL AND [ResponsibleVersion] IS NOT NULL)) AND (([PaymentTermsId] IS NULL AND [PaymentTermsVersion] IS NULL) OR ([PaymentTermsId] IS NOT NULL AND [PaymentTermsVersion] IS NOT NULL)) "
                    + "AND (([SourceStrategyId] IS NULL AND [SourceStrategyVersion] IS NULL) OR ([SourceStrategyId] IS NOT NULL AND [SourceStrategyVersion] IS NOT NULL)) AND (([PackageId] IS NULL AND [PackageVersion] IS NULL) OR ([PackageId] IS NOT NULL AND [PackageVersion] IS NOT NULL))");
                table.ForeignKey(
                    name: "FK_Drafts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DraftLines",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ProductCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ProductName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ProductEffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                StockUnitCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StockUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StockUnitProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StockUnitProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                SalesUnitCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesUnitProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesUnitProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderUnitBindingCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitBindingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitBindingProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitBindingProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderUnitCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OrderUnitDimensionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OrderUnitProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderUnitIsKilogram = table.Column<bool>(type: "bit", nullable: false),
                OrderUnitProfileScale = table.Column<int>(type: "int", nullable: false),
                OrderUnitScale = table.Column<int>(type: "int", nullable: false),
                OrderUnitIsActive = table.Column<bool>(type: "bit", nullable: false),
                OrderUnitEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                OrderUnitEffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                QuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                UnitPrice = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                PriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DiscountPercent = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftLines", x => new { x.CompanyId, x.DraftId, x.LineNo });
                table.CheckConstraint("CK_DraftLines_LineNo", "[LineNo] >= 1");
                table.CheckConstraint("CK_DraftLines_Identity",
                    "[LineId] <> " + NotEmptyGuid + " AND [ProductId] <> " + NotEmptyGuid + " "
                    + "AND [OrderUnitId] <> " + NotEmptyGuid + " AND [OrderUnitProfileId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_DraftLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
                table.CheckConstraint("CK_DraftLines_Scale",
                    "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
                table.CheckConstraint("CK_DraftLines_Price", "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
                table.CheckConstraint("CK_DraftLines_Window",
                    "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
                table.ForeignKey(
                    name: "FK_DraftLines_Drafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "sales",
                    principalTable: "Drafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DraftDeliverySlices",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                SliceNo = table.Column<int>(type: "int", nullable: false),
                SliceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                QuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftDeliverySlices", x => new { x.CompanyId, x.DraftId, x.LineNo, x.SliceNo });
                table.CheckConstraint("CK_DraftDeliverySlices_SliceNo", "[SliceNo] >= 1");
                table.CheckConstraint("CK_DraftDeliverySlices_Identity",
                    "[SliceId] <> " + NotEmptyGuid + " AND [OrderUnitId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_DraftDeliverySlices_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
                table.ForeignKey(
                    name: "FK_DraftDeliverySlices_DraftLines_CompanyId_DraftId_LineNo",
                    columns: x => new { x.CompanyId, x.DraftId, x.LineNo },
                    principalSchema: "sales",
                    principalTable: "DraftLines",
                    principalColumns: new[] { "CompanyId", "DraftId", "LineNo" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DraftAttempts",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftAttempts", x => new { x.CompanyId, x.IdempotencyKey });
                table.CheckConstraint("CK_DraftAttempts_Identity",
                    "[CompanyId] <> " + NotEmptyGuid + " AND LEN([IdempotencyKey]) > 0 "
                    + "AND [DraftId] <> " + NotEmptyGuid + " AND LEN([CorrelationId]) > 0 AND LEN([RecordedBy]) > 0");
                table.CheckConstraint("CK_DraftAttempts_RequestHash", "LEN([RequestHash]) = 64");
                table.ForeignKey(
                    name: "FK_DraftAttempts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DraftAttempts_Drafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "sales",
                    principalTable: "Drafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_Drafts_DocumentNo",
            schema: "sales",
            table: "Drafts",
            columns: new[] { "CompanyId", "DocumentNo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_CompanyId_CreatedAtUtc_Id",
            schema: "sales",
            table: "Drafts",
            columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "UX_DraftLines_LineId",
            schema: "sales",
            table: "DraftLines",
            columns: new[] { "CompanyId", "DraftId", "LineId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_DraftDeliverySlices_SliceId",
            schema: "sales",
            table: "DraftDeliverySlices",
            columns: new[] { "CompanyId", "DraftId", "LineNo", "SliceId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_DraftAttempts_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DraftAttempts", schema: "sales");
        migrationBuilder.DropTable(name: "DraftDeliverySlices", schema: "sales");
        migrationBuilder.DropTable(name: "DraftLines", schema: "sales");
        migrationBuilder.DropTable(name: "Drafts", schema: "sales");
        migrationBuilder.DropTable(name: "DraftNumbers", schema: "sales");
    }
}
