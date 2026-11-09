using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OpenItems_Status",
                schema: "finance",
                table: "OpenItems");

            migrationBuilder.CreateTable(
                name: "InvoiceCancellations",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCancellations", x => x.Id);
                    table.UniqueConstraint("AK_InvoiceCancellations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_InvoiceCancellations_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_InvoiceCancellations_Text", "LEN([SourceKind]) > 0 AND LEN([InvoiceVersion]) > 0 AND LEN([Reason]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_InvoiceCancellations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceCancellationItems",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversedAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceCancellationItems", x => new { x.CompanyId, x.OpenItemId });
                    table.CheckConstraint("CK_InvoiceCancellationItems_Amount", "LEN([ReversedAmountText]) > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceCancellationItems_InvoiceCancellations_CompanyId_CancellationId",
                        columns: x => new { x.CompanyId, x.CancellationId },
                        principalSchema: "finance",
                        principalTable: "InvoiceCancellations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceCancellationItems_OpenItems_CompanyId_OpenItemId",
                        columns: x => new { x.CompanyId, x.OpenItemId },
                        principalSchema: "finance",
                        principalTable: "OpenItems",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OpenItems_Status",
                schema: "finance",
                table: "OpenItems",
                sql: "[Status] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCancellationItems_CompanyId_CancellationId",
                schema: "finance",
                table: "InvoiceCancellationItems",
                columns: new[] { "CompanyId", "CancellationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCancellations_CompanyId_IdempotencyKeyHash",
                schema: "finance",
                table: "InvoiceCancellations",
                columns: new[] { "CompanyId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceCancellations_CompanyId_SourceKind_InvoiceId",
                schema: "finance",
                table: "InvoiceCancellations",
                columns: new[] { "CompanyId", "SourceKind", "InvoiceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51051): a rollback never drops a recorded invoice cancellation or its reversal.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[InvoiceCancellations]) OR EXISTS (SELECT 1 FROM [finance].[OpenItems] WHERE [Status] = 3)
                    THROW 51051, 'Finance invoice cancellations exist; AddInvoiceCancellations is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "InvoiceCancellationItems",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "InvoiceCancellations",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OpenItems_Status",
                schema: "finance",
                table: "OpenItems");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OpenItems_Status",
                schema: "finance",
                table: "OpenItems",
                sql: "[Status] IN (1, 2)");
        }
    }
}
