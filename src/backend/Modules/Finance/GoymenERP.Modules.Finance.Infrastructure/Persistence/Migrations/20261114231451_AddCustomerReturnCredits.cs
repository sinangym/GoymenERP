using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturnCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerReturnCredits",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalInvoiceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    AppliedAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    AdvanceAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    FunctionalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnCredits", x => x.Id);
                    table.UniqueConstraint("AK_CustomerReturnCredits_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CustomerReturnCredits_Functional", "[FunctionalAmountText] IS NULL OR LEN([FunctionalAmountText]) > 0");
                    table.CheckConstraint("CK_CustomerReturnCredits_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalInvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CustomerReturnCredits_Text", "LEN([SourceKind]) > 0 AND LEN([DocumentVersion]) > 0 AND LEN([OriginalInvoiceVersion]) > 0 AND LEN([AmountText]) > 0 AND LEN([AppliedAmountText]) > 0 AND LEN([AdvanceAmountText]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_CustomerReturnCredits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnInvoiceStates",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnedAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnInvoiceStates", x => new { x.CompanyId, x.OriginalInvoiceId });
                    table.CheckConstraint("CK_CustomerReturnInvoiceStates_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalInvoiceId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CustomerReturnInvoiceStates_Text", "LEN([ReturnedAmountText]) > 0 AND LEN([UpdatedBy]) > 0");
                    table.CheckConstraint("CK_CustomerReturnInvoiceStates_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_CustomerReturnInvoiceStates_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnCreditItems",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReducedAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnCreditItems", x => new { x.CompanyId, x.CreditId, x.OpenItemId });
                    table.CheckConstraint("CK_CustomerReturnCreditItems_Amount", "LEN([ReducedAmountText]) > 0");
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditItems_CustomerReturnCredits_CompanyId_CreditId",
                        columns: x => new { x.CompanyId, x.CreditId },
                        principalSchema: "finance",
                        principalTable: "CustomerReturnCredits",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnCreditItems_OpenItems_CompanyId_OpenItemId",
                        columns: x => new { x.CompanyId, x.OpenItemId },
                        principalSchema: "finance",
                        principalTable: "OpenItems",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCreditItems_CompanyId_OpenItemId",
                schema: "finance",
                table: "CustomerReturnCreditItems",
                columns: new[] { "CompanyId", "OpenItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CompanyId_IdempotencyKeyHash",
                schema: "finance",
                table: "CustomerReturnCredits",
                columns: new[] { "CompanyId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CompanyId_OriginalInvoiceId",
                schema: "finance",
                table: "CustomerReturnCredits",
                columns: new[] { "CompanyId", "OriginalInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnCredits_CompanyId_SourceKind_DocumentId",
                schema: "finance",
                table: "CustomerReturnCredits",
                columns: new[] { "CompanyId", "SourceKind", "DocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51320), forward fix: a stored customer return credit (F-019, SAT-D291) reduced
            // open items and may hold the customer's advance; dropping its tables would lose that reversing record while
            // the reduced outstanding amounts stay, so the rollback refuses while any credit or invoice return state exists.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [finance].[CustomerReturnCredits])
                    OR EXISTS (SELECT 1 FROM [finance].[CustomerReturnInvoiceStates])
                BEGIN
                    THROW 51320, 'Finance customer return credits are stored; AddCustomerReturnCredits is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CustomerReturnCreditItems",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CustomerReturnInvoiceStates",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CustomerReturnCredits",
                schema: "finance");
        }
    }
}
