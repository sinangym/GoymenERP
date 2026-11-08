using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "sales",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecidedAtUtc",
                schema: "sales",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedBy",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAtUtc",
                schema: "sales",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices",
                sql: "(([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND [SubmittedAtUtc] IS NOT NULL)) AND (([State] = 1 AND [SubmittedBy] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 3 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 5 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_MakerChecker",
                schema: "sales",
                table: "Invoices",
                sql: "[DecidedBy] IS NULL OR ([DecidedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Reasons",
                schema: "sales",
                table: "Invoices",
                sql: "([DecisionReason] IS NULL OR LEN([DecisionReason]) > 0) AND ([CancellationReason] IS NULL OR LEN([CancellationReason]) > 0) AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0) AND ([DecidedBy] IS NULL OR LEN([DecidedBy]) > 0) AND ([CancelledBy] IS NULL OR LEN([CancelledBy]) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices",
                sql: "[State] IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts",
                sql: "[CommandName] IN (N'sales.invoice.create.v1', N'sales.invoice.submit.v1', N'sales.invoice.approve.v1', N'sales.invoice.reject.v1', N'sales.invoice.cancel.v1')");

            // SAT-D88/D204 + AT-055: a decided, rejected or cancelled invoice is immutable in SQL beside the domain guard (51223 header, 51224 lines;
            // 51222 is the Down guard). Only the draft and pre-approval states may still change; a line may only gain its release stamp, which the
            // header trigger sets when the invoice is rejected or cancelled.
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_Invoices_Guard]
                ON [sales].[Invoices]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF NOT EXISTS (SELECT 1 FROM inserted)
                    BEGIN
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51223, 'Sales invoices are never deleted; cancel or reject them instead.', 1;
                        RETURN;
                    END;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [State] IN (3, 4, 5))
                        THROW 51223, 'A decided, rejected or cancelled sales invoice is immutable.', 1;
                    IF EXISTS (SELECT [Id], [CompanyId], [DocumentNo], [BusinessDate], [PartyId], [CompanyAccountId], [CurrencyId], [MoneyProfileId], [MoneyProfileVersion], [AmountScale], [ExchangeRate], [RateDay], [RateBulletinDate], [FunctionalCurrencyId], [FunctionalScale], [InstalmentsJson], [CreatedBy], [CreatedAtUtc], [CorrelationId] FROM deleted EXCEPT SELECT [Id], [CompanyId], [DocumentNo], [BusinessDate], [PartyId], [CompanyAccountId], [CurrencyId], [MoneyProfileId], [MoneyProfileVersion], [AmountScale], [ExchangeRate], [RateDay], [RateBulletinDate], [FunctionalCurrencyId], [FunctionalScale], [InstalmentsJson], [CreatedBy], [CreatedAtUtc], [CorrelationId] FROM inserted)
                        THROW 51223, 'The identity, amount and rate facts of a sales invoice are immutable.', 1;
                    -- AT-055 / SAT-D94: a rejected or cancelled invoice releases its shares in the SAME statement that decides it. EF writes
                    -- independent tables in table-name order (the lines before the header), so the release stamp is the header's, not the app's.
                    IF EXISTS (SELECT 1 FROM inserted WHERE [State] IN (4, 5))
                        UPDATE l SET l.[ReleasedAtUtc] = COALESCE(i.[CancelledAtUtc], i.[DecidedAtUtc])
                        FROM [sales].[InvoiceLines] l
                        JOIN inserted i ON i.[CompanyId] = l.[CompanyId] AND i.[Id] = l.[InvoiceId]
                        WHERE i.[State] IN (4, 5) AND l.[ReleasedAtUtc] IS NULL;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_InvoiceLines_Guard]
                ON [sales].[InvoiceLines]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF NOT EXISTS (SELECT 1 FROM inserted)
                    BEGIN
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51224, 'Sales invoice lines are never deleted; the share is released instead.', 1;
                        RETURN;
                    END;
                    IF EXISTS (SELECT [CompanyId], [InvoiceId], [LineNo], [LineId], [ShipmentId], [DispatchNoteId], [DispatchNoteNumber], [NoteLineNo], [ShipmentDate], [SalesOrderId], [SalesOrderLineId], [PriceBasisRevision], [ProductId], [UnitId], [Quantity], [QuantityText], [UnitPrice], [PriceUnitId], [DiscountPercent], [PricingJson], [TaxJson], [NetUnitPrice], [VatPercent], [WithholdingShare], [NetAmount], [VatAmount], [WithholdingAmount], [FunctionalNetAmount], [FunctionalVatAmount], [FunctionalWithholdingAmount], [FunctionalAdjusted], [FunctionalNeedsReview] FROM deleted EXCEPT SELECT [CompanyId], [InvoiceId], [LineNo], [LineId], [ShipmentId], [DispatchNoteId], [DispatchNoteNumber], [NoteLineNo], [ShipmentDate], [SalesOrderId], [SalesOrderLineId], [PriceBasisRevision], [ProductId], [UnitId], [Quantity], [QuantityText], [UnitPrice], [PriceUnitId], [DiscountPercent], [PricingJson], [TaxJson], [NetUnitPrice], [VatPercent], [WithholdingShare], [NetAmount], [VatAmount], [WithholdingAmount], [FunctionalNetAmount], [FunctionalVatAmount], [FunctionalWithholdingAmount], [FunctionalAdjusted], [FunctionalNeedsReview] FROM inserted)
                        THROW 51224, 'A sales invoice line is immutable but for its release stamp.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId] AND i.[LineNo] = d.[LineNo]
                               WHERE d.[ReleasedAtUtc] IS NOT NULL AND (i.[ReleasedAtUtc] IS NULL OR i.[ReleasedAtUtc] <> d.[ReleasedAtUtc]))
                        THROW 51224, 'A released sales invoice line stays released.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId] AND i.[LineNo] = d.[LineNo]
                               WHERE d.[ReleasedAtUtc] IS NULL AND i.[ReleasedAtUtc] IS NOT NULL
                                 AND NOT EXISTS (SELECT 1 FROM [sales].[Invoices] v WHERE v.[CompanyId] = i.[CompanyId] AND v.[Id] = i.[InvoiceId] AND v.[State] IN (4, 5)))
                        THROW 51224, 'A share is released only by a rejected or cancelled invoice.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once an invoice left the draft state the rows carry decisions, reasons and released shares that the
            // previous model cannot hold. The guard refuses (51222) before anything is dropped, then the two triggers go (same batch).
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[Invoices] WHERE [State] <> 1)
                   OR EXISTS (SELECT 1 FROM [sales].[InvoiceAttempts] WHERE [CommandName] <> N'sales.invoice.create.v1')
                BEGIN
                    THROW 51222, 'Sales invoices were submitted, decided or cancelled; AddSalesInvoiceApproval is not rolled back (forward fix).', 1;
                END;
                DROP TRIGGER [sales].[TR_InvoiceLines_Guard];
                DROP TRIGGER [sales].[TR_Invoices_Guard];
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_MakerChecker",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Reasons",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DecisionReason",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices",
                sql: "[State] IN (1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts",
                sql: "[CommandName] IN (N'sales.invoice.create.v1')");
        }
    }
}
