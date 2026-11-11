using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturnRejectionAndCancellation : Migration
    {
        // SAT-D290 (2, 3): a pending return can be rejected and a draft or pending return cancelled, each with an optional reason. Six
        // nullable fact columns; no existing row changes (every stored return is in states 1-4 with the new facts null). States 4, 5
        // and 6 are terminal; TR_CustomerReturns_Guard (51255) now allows exactly 1->2, 2->3, 3->4, 2->5, 1->6 and 2->6. Down guard
        // THROW 51259.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_Facts",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_MakerChecker",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturnAttempts_Command",
                schema: "sales",
                table: "CustomerReturnAttempts");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "sales",
                table: "CustomerReturns",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "sales",
                table: "CustomerReturns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                schema: "sales",
                table: "CustomerReturns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAtUtc",
                schema: "sales",
                table: "CustomerReturns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                schema: "sales",
                table: "CustomerReturns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "sales",
                table: "CustomerReturns",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_Facts",
                schema: "sales",
                table: "CustomerReturns",
                sql: "([RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL AND (([State] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 1) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 2) OR ([State] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 3) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [FinalizedBy] IS NOT NULL AND LEN([FinalizedBy]) > 0 AND [FinalizedAtUtc] IS NOT NULL AND [FinalizationSequence] >= 1 AND [Version] >= 4))) OR ([State] = 5 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [RejectedBy] IS NOT NULL AND LEN([RejectedBy]) > 0 AND [RejectedAtUtc] IS NOT NULL AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0) AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL AND [Version] >= 3) OR ([State] = 6 AND (([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL)) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL AND ([CancellationReason] IS NULL OR LEN([CancellationReason]) > 0) AND [Version] >= 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_MakerChecker",
                schema: "sales",
                table: "CustomerReturns",
                sql: "([ApprovedBy] IS NULL OR ([ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2)) AND ([RejectedBy] IS NULL OR ([RejectedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [RejectedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "sales",
                table: "CustomerReturns",
                sql: "[State] IN (1, 2, 3, 4, 5, 6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturnAttempts_Command",
                schema: "sales",
                table: "CustomerReturnAttempts",
                sql: "[CommandName] IN (N'sales.customer-return.create.v1', N'sales.customer-return.submit.v1', N'sales.customer-return.approve.v1', N'sales.customer-return.finalize.v1', N'sales.customer-return.reject.v1', N'sales.customer-return.cancel.v1')");

            // The steps a return may take; the immutable columns and the earlier steps' facts stay as they were (51255).
            migrationBuilder.Sql("""
                ALTER TRIGGER [sales].[TR_CustomerReturns_Guard]
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
                           OR NOT ((d.[State] = 1 AND i.[State] IN (2, 6))
                                   OR (d.[State] = 2 AND i.[State] IN (3, 5, 6))
                                   OR (d.[State] = 3 AND i.[State] = 4))
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
                        THROW 51255, 'A sales customer return keeps its document, lines and totals and takes one allowed step only.', 1;
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a rejected or cancelled return, or an attempt of the new commands, is evidence that the previous model cannot
            // hold. The guard refuses (51259) before anything is dropped.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[CustomerReturns] WHERE [State] IN (5, 6))
                   OR EXISTS (SELECT 1 FROM [sales].[CustomerReturnAttempts]
                              WHERE [CommandName] IN (N'sales.customer-return.reject.v1', N'sales.customer-return.cancel.v1'))
                BEGIN
                    THROW 51259, 'Sales customer returns were rejected or cancelled; AddCustomerReturnRejectionAndCancellation is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_Facts",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_MakerChecker",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturnAttempts_Command",
                schema: "sales",
                table: "CustomerReturnAttempts");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "sales",
                table: "CustomerReturns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_Facts",
                schema: "sales",
                table: "CustomerReturns",
                sql: "([State] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 1) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 2) OR ([State] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [FinalizedBy] IS NULL AND [FinalizedAtUtc] IS NULL AND [FinalizationSequence] IS NULL AND [Version] >= 3) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [FinalizedBy] IS NOT NULL AND LEN([FinalizedBy]) > 0 AND [FinalizedAtUtc] IS NOT NULL AND [FinalizationSequence] >= 1 AND [Version] >= 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_MakerChecker",
                schema: "sales",
                table: "CustomerReturns",
                sql: "[ApprovedBy] IS NULL OR ([ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "sales",
                table: "CustomerReturns",
                sql: "[State] IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturnAttempts_Command",
                schema: "sales",
                table: "CustomerReturnAttempts",
                sql: "[CommandName] IN (N'sales.customer-return.create.v1', N'sales.customer-return.submit.v1', N'sales.customer-return.approve.v1', N'sales.customer-return.finalize.v1')");

            // The previous trigger: one step forward only.
            migrationBuilder.Sql("""
                ALTER TRIGGER [sales].[TR_CustomerReturns_Guard]
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
        }
    }
}
