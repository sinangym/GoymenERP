using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MoneyProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AmountScale = table.Column<int>(type: "int", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    RateDay = table.Column<DateOnly>(type: "date", nullable: true),
                    RateBulletinDate = table.Column<DateOnly>(type: "date", nullable: true),
                    FunctionalCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FunctionalScale = table.Column<int>(type: "int", nullable: true),
                    InstalmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.UniqueConstraint("AK_Invoices_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Invoices_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Invoices_Instalments", "ISJSON([InstalmentsJson]) = 1");
                    table.CheckConstraint("CK_Invoices_Rate", "([ExchangeRate] IS NULL AND [RateDay] IS NULL AND [RateBulletinDate] IS NULL AND [FunctionalCurrencyId] IS NULL AND [FunctionalScale] IS NULL) OR ([ExchangeRate] > 0 AND [RateDay] IS NOT NULL AND [RateBulletinDate] IS NOT NULL AND [FunctionalCurrencyId] IS NOT NULL AND [FunctionalCurrencyId] <> [CurrencyId] AND [FunctionalScale] >= 0 AND [FunctionalScale] <= 8)");
                    table.CheckConstraint("CK_Invoices_Scale", "[AmountScale] >= 0 AND [AmountScale] <= 8");
                    table.CheckConstraint("CK_Invoices_State", "[State] IN (1)");
                    table.CheckConstraint("CK_Invoices_Text", "LEN([DocumentNo]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_Invoices_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_Invoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_InvoiceAttempts_Command", "[CommandName] IN (N'sales.invoice.create.v1')");
                    table.CheckConstraint("CK_InvoiceAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_InvoiceAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_InvoiceAttempts_Outcome", "([Outcome] = 1 AND [InvoiceId] IS NOT NULL AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_InvoiceAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceAttempts_Invoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchNoteNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NoteLineNo = table.Column<int>(type: "int", nullable: false),
                    ShipmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceBasisRevision = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnitPrice = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscountPercent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PricingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaxJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NetUnitPrice = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    VatPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    WithholdingShare = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    FunctionalNetAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FunctionalVatAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FunctionalWithholdingAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FunctionalAdjusted = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    FunctionalNeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => new { x.CompanyId, x.InvoiceId, x.LineNo });
                    table.CheckConstraint("CK_InvoiceLines_Amounts", "LEN([NetUnitPrice]) > 0 AND [VatPercent] >= 0 AND [VatPercent] <= 100 AND ([WithholdingShare] IS NULL OR ([WithholdingShare] > 0 AND [WithholdingShare] <= 1)) AND [NetAmount] >= 0 AND [VatAmount] >= 0 AND [WithholdingAmount] >= 0");
                    table.CheckConstraint("CK_InvoiceLines_Functional", "([FunctionalNetAmount] IS NULL AND [FunctionalVatAmount] IS NULL AND [FunctionalWithholdingAmount] IS NULL AND [FunctionalAdjusted] IS NULL AND [FunctionalNeedsReview] = 0) OR ([FunctionalNetAmount] IS NOT NULL AND [FunctionalVatAmount] IS NOT NULL AND [FunctionalWithholdingAmount] IS NOT NULL)");
                    table.CheckConstraint("CK_InvoiceLines_Identity", "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [DispatchNoteId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_InvoiceLines_Json", "ISJSON([TaxJson]) = 1 AND ([PricingJson] IS NULL OR ISJSON([PricingJson]) = 1)");
                    table.CheckConstraint("CK_InvoiceLines_LineNo", "[LineNo] >= 1 AND [NoteLineNo] >= 1 AND [PriceBasisRevision] >= 0");
                    table.CheckConstraint("CK_InvoiceLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0 AND LEN([DispatchNoteNumber]) > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_InvoiceAttempts_InvoiceId",
                schema: "sales",
                table: "InvoiceAttempts",
                columns: new[] { "CompanyId", "InvoiceId" },
                unique: true,
                filter: "[InvoiceId] IS NOT NULL AND [CommandName] = N'sales.invoice.create.v1'");

            migrationBuilder.CreateIndex(
                name: "UX_InvoiceLines_ActiveShipment",
                schema: "sales",
                table: "InvoiceLines",
                columns: new[] { "CompanyId", "ShipmentId" },
                unique: true,
                filter: "[ReleasedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InvoiceLines_LineId",
                schema: "sales",
                table: "InvoiceLines",
                columns: new[] { "CompanyId", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId_CreatedAtUtc_Id",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Invoices_DocumentNo",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "CompanyId", "DocumentNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored invoice draft (or its attempt) would lose its record; Down refuses while one exists.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[Invoices]) OR EXISTS (SELECT 1 FROM [sales].[InvoiceAttempts])
                BEGIN
                    THROW 51221, 'Sales invoice drafts exist; AddSalesInvoices is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "InvoiceAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "InvoiceLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "sales");
        }
    }
}
