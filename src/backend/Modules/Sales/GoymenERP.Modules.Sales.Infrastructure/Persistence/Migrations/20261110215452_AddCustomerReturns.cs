using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturns : Migration
    {
        // API-40 (EK57 SAT-D288…D294): the recorded customer return invoice against an original sales invoice and its own append-only
        // attempt records. Two new tables only; no existing row changes. The header, the customer's document, the lines and the totals
        // never change, the state moves one step forward and the version by one (TR_CustomerReturns_Guard, 51255); the attempts are
        // append-only (TR_CustomerReturnAttempts_Guard, 51256). Down guard THROW 51254.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerReturns",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalDocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountScale = table.Column<int>(type: "int", nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: true),
                    FunctionalCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FunctionalScale = table.Column<int>(type: "int", nullable: true),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerDocumentNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CustomerDocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalizedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalizationSequence = table.Column<int>(type: "int", nullable: true),
                    NetAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    WithholdingAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    LineTotalAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    FunctionalNetAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FunctionalVatAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FunctionalWithholdingAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FunctionalLineTotalAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    LinesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturns", x => x.Id);
                    table.UniqueConstraint("AK_CustomerReturns_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CustomerReturns_Facts", "([State] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 1) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 2) OR ([State] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 3) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [FinalizedBy] IS NOT NULL AND LEN([FinalizedBy]) > 0 AND [FinalizedAtUtc] IS NOT NULL AND [FinalizationSequence] >= 1 AND [Version] >= 4)");
                    table.CheckConstraint("CK_CustomerReturns_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OriginalInvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND ([ShipmentId] IS NULL OR [ShipmentId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_CustomerReturns_Lines", "ISJSON([LinesJson]) = 1 AND [LineCount] >= 1");
                    table.CheckConstraint("CK_CustomerReturns_MakerChecker", "[ApprovedBy] IS NULL OR ([ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_CustomerReturns_Rate", "([ExchangeRate] IS NULL AND [FunctionalCurrencyId] IS NULL AND [FunctionalScale] IS NULL AND [FunctionalNetAmount] IS NULL AND [FunctionalVatAmount] IS NULL AND [FunctionalWithholdingAmount] IS NULL AND [FunctionalLineTotalAmount] IS NULL) OR ([ExchangeRate] > 0 AND [FunctionalCurrencyId] IS NOT NULL AND [FunctionalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [FunctionalScale] >= 0 AND [FunctionalScale] <= 8 AND [FunctionalNetAmount] IS NOT NULL AND [FunctionalVatAmount] IS NOT NULL AND [FunctionalWithholdingAmount] IS NOT NULL AND [FunctionalLineTotalAmount] IS NOT NULL)");
                    table.CheckConstraint("CK_CustomerReturns_Scale", "[AmountScale] >= 0 AND [AmountScale] <= 8");
                    table.CheckConstraint("CK_CustomerReturns_State", "[State] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_CustomerReturns_Text", "LEN([OriginalDocumentNo]) > 0 AND LEN([CustomerDocumentNo]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_CustomerReturns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturns_Invoices_CompanyId_OriginalInvoiceId",
                        columns: x => new { x.CompanyId, x.OriginalInvoiceId },
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReturnAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    CustomerReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReturnAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_CustomerReturnAttempts_Command", "[CommandName] IN (N'sales.customer-return.create.v1', N'sales.customer-return.submit.v1', N'sales.customer-return.approve.v1', N'sales.customer-return.finalize.v1')");
                    table.CheckConstraint("CK_CustomerReturnAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_CustomerReturnAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0 AND ([TargetId] IS NULL OR [TargetId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_CustomerReturnAttempts_Outcome", "([Outcome] = 1 AND [CustomerReturnId] IS NOT NULL AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [CustomerReturnId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_CustomerReturnAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReturnAttempts_CustomerReturns_CompanyId_CustomerReturnId",
                        columns: x => new { x.CompanyId, x.CustomerReturnId },
                        principalSchema: "sales",
                        principalTable: "CustomerReturns",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturnAttempts_CompanyId_CustomerReturnId",
                schema: "sales",
                table: "CustomerReturnAttempts",
                columns: new[] { "CompanyId", "CustomerReturnId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReturns_Created",
                schema: "sales",
                table: "CustomerReturns",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CustomerReturns_CustomerDocument",
                schema: "sales",
                table: "CustomerReturns",
                columns: new[] { "CompanyId", "PartyId", "CustomerDocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CustomerReturns_FinalizationSequence",
                schema: "sales",
                table: "CustomerReturns",
                columns: new[] { "CompanyId", "OriginalInvoiceId", "FinalizationSequence" },
                unique: true,
                filter: "[FinalizationSequence] IS NOT NULL");

            // SAT-D288/D291/D292: the customer's document, the copied lines and their totals are evidence; a return takes exactly one
            // step forward (Draft -> Submitted -> Approved -> Finalized), the facts of an earlier step never change, the version moves by
            // exactly one, and nothing is deleted (51255).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_CustomerReturns_Guard]
                ON [sales].[CustomerReturns]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted AS d
                        LEFT JOIN inserted AS i ON i.[Id] = d.[Id]
                        WHERE i.[Id] IS NULL
                           OR d.[State] >= 4
                           OR i.[State] <> d.[State] + 1
                           OR i.[Version] <> d.[Version] + 1
                           OR EXISTS (
                               SELECT d.[CompanyId], d.[OriginalInvoiceId], d.[OriginalDocumentNo] COLLATE Latin1_General_100_BIN2, d.[PartyId],
                                      d.[CompanyAccountId], d.[CurrencyId], d.[AmountScale], d.[ExchangeRate], d.[FunctionalCurrencyId],
                                      d.[FunctionalScale], d.[ShipmentId], d.[CustomerDocumentNo], d.[CustomerDocumentDate],
                                      d.[CreatedBy] COLLATE Latin1_General_100_BIN2, d.[CreatedAtUtc], d.[NetAmount], d.[VatAmount],
                                      d.[WithholdingAmount], d.[LineTotalAmount], d.[FunctionalNetAmount], d.[FunctionalVatAmount],
                                      d.[FunctionalWithholdingAmount], d.[FunctionalLineTotalAmount], d.[NeedsReview], d.[LineCount],
                                      d.[LinesJson] COLLATE Latin1_General_100_BIN2, d.[CorrelationId] COLLATE Latin1_General_100_BIN2
                               EXCEPT
                               SELECT i.[CompanyId], i.[OriginalInvoiceId], i.[OriginalDocumentNo] COLLATE Latin1_General_100_BIN2, i.[PartyId],
                                      i.[CompanyAccountId], i.[CurrencyId], i.[AmountScale], i.[ExchangeRate], i.[FunctionalCurrencyId],
                                      i.[FunctionalScale], i.[ShipmentId], i.[CustomerDocumentNo], i.[CustomerDocumentDate],
                                      i.[CreatedBy] COLLATE Latin1_General_100_BIN2, i.[CreatedAtUtc], i.[NetAmount], i.[VatAmount],
                                      i.[WithholdingAmount], i.[LineTotalAmount], i.[FunctionalNetAmount], i.[FunctionalVatAmount],
                                      i.[FunctionalWithholdingAmount], i.[FunctionalLineTotalAmount], i.[NeedsReview], i.[LineCount],
                                      i.[LinesJson] COLLATE Latin1_General_100_BIN2, i.[CorrelationId] COLLATE Latin1_General_100_BIN2)
                           OR (d.[State] >= 2 AND EXISTS (
                               SELECT d.[SubmittedBy] COLLATE Latin1_General_100_BIN2, d.[SubmittedAtUtc]
                               EXCEPT
                               SELECT i.[SubmittedBy] COLLATE Latin1_General_100_BIN2, i.[SubmittedAtUtc]))
                           OR (d.[State] >= 3 AND EXISTS (
                               SELECT d.[ApprovedBy] COLLATE Latin1_General_100_BIN2, d.[ApprovedAtUtc]
                               EXCEPT
                               SELECT i.[ApprovedBy] COLLATE Latin1_General_100_BIN2, i.[ApprovedAtUtc])))
                    BEGIN
                        THROW 51255, 'A sales customer return keeps its document, lines and totals and moves one step forward only.', 1;
                    END;
                END;
                """);

            // ADR-011: the attempt records are append-only (51256).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_CustomerReturnAttempts_Guard]
                ON [sales].[CustomerReturnAttempts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51256, 'Sales customer return attempts are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded customer return (or a recorded attempt) is evidence that the previous model cannot hold. The guard
            // refuses (51254) before anything is dropped; dropping a table drops its trigger.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[CustomerReturns])
                   OR EXISTS (SELECT 1 FROM [sales].[CustomerReturnAttempts])
                BEGIN
                    THROW 51254, 'Sales customer returns were recorded; AddCustomerReturns is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CustomerReturnAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "CustomerReturns",
                schema: "sales");
        }
    }
}
