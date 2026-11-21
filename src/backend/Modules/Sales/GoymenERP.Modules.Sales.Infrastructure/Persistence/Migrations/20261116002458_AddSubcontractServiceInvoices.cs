using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractServiceInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubcontractServiceInvoices",
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
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoadingInstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeighingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeighingRevision = table.Column<int>(type: "int", nullable: false),
                    InstructionVersion = table.Column<long>(type: "bigint", nullable: false),
                    NetKgText = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractServiceInvoices", x => x.Id);
                    table.UniqueConstraint("AK_SubcontractServiceInvoices_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SubcontractServiceInvoices_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [LoadingInstructionId] <> '00000000-0000-0000-0000-000000000000' AND [WeighingId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SubcontractServiceInvoices_Scale", "[AmountScale] >= 0 AND [AmountScale] <= 8");
                    table.CheckConstraint("CK_SubcontractServiceInvoices_Source", "[WeighingRevision] >= 1 AND [InstructionVersion] >= 1 AND LEN([NetKgText]) > 0");
                    table.CheckConstraint("CK_SubcontractServiceInvoices_State", "[State] = 1");
                    table.CheckConstraint("CK_SubcontractServiceInvoices_Text", "LEN([DocumentNo]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_SubcontractServiceInvoices_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_SubcontractServiceInvoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubcontractServiceInvoiceAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractServiceInvoiceAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_SubcontractServiceInvoiceAttempts_Command", "[CommandName] = N'sales.subcontract-service-invoice.create.v1'");
                    table.CheckConstraint("CK_SubcontractServiceInvoiceAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_SubcontractServiceInvoiceAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_SubcontractServiceInvoiceAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubcontractServiceInvoiceAttempts_SubcontractServiceInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "SubcontractServiceInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubcontractServiceInvoiceLines",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    WithholdingAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractServiceInvoiceLines", x => new { x.CompanyId, x.InvoiceId, x.LineNo });
                    table.CheckConstraint("CK_SubcontractServiceInvoiceLines_Amounts", "LEN([NetUnitPrice]) > 0 AND [VatPercent] >= 0 AND [VatPercent] <= 100 AND ([WithholdingShare] IS NULL OR ([WithholdingShare] > 0 AND [WithholdingShare] <= 1)) AND [NetAmount] >= 0 AND [VatAmount] >= 0 AND [WithholdingAmount] >= 0");
                    table.CheckConstraint("CK_SubcontractServiceInvoiceLines_Identity", "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SubcontractServiceInvoiceLines_Json", "ISJSON([TaxJson]) = 1 AND ([PricingJson] IS NULL OR ISJSON([PricingJson]) = 1)");
                    table.CheckConstraint("CK_SubcontractServiceInvoiceLines_LineNo", "[LineNo] >= 1 AND [PriceBasisRevision] >= 0");
                    table.CheckConstraint("CK_SubcontractServiceInvoiceLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
                    table.ForeignKey(
                        name: "FK_SubcontractServiceInvoiceLines_SubcontractServiceInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "SubcontractServiceInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SubcontractServiceInvoiceAttempts_InvoiceId",
                schema: "sales",
                table: "SubcontractServiceInvoiceAttempts",
                columns: new[] { "CompanyId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SubcontractServiceInvoiceLines_LineId",
                schema: "sales",
                table: "SubcontractServiceInvoiceLines",
                columns: new[] { "CompanyId", "LineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubcontractServiceInvoices_Order",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                columns: new[] { "CompanyId", "SalesOrderId" });

            migrationBuilder.CreateIndex(
                name: "UX_SubcontractServiceInvoices_DocumentNo",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                columns: new[] { "CompanyId", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SubcontractServiceInvoices_Weighing",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                columns: new[] { "CompanyId", "LoadingInstructionId", "WeighingId", "SalesOrderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded subcontract service invoice draft is never dropped by a rollback; an empty table rolls back.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [sales].[SubcontractServiceInvoices]) BEGIN THROW 51732, 'Subcontract service invoices exist; AddSubcontractServiceInvoices is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "SubcontractServiceInvoiceAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SubcontractServiceInvoiceLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SubcontractServiceInvoices",
                schema: "sales");
        }
    }
}
