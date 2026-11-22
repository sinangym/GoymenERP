using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

// SAT-D434 (AK-4): migration-only SQL, fixed owner table, no runtime SQL surface. The invoice line guard is the same trigger as
// SalesOfficialInvoiceCancellationGuards' with the piece line's kg information (TheoreticalKg, ActualKg) added to the immutable
// column list; Down restores the previous body verbatim.
internal static class SalesPieceLineKilogramGuards
{
    internal const string DownGuardSql = """
        IF EXISTS (SELECT 1 FROM [sales].[DraftLineKilogramsPerUnit])
           OR EXISTS (SELECT 1 FROM [sales].[InvoiceLines] WHERE [TheoreticalKg] IS NOT NULL OR [ActualKg] IS NOT NULL)
        BEGIN
            THROW 52051, 'Pinned piece-line kilograms per unit or invoice kilogram information exist; AddSalesPieceLineKilograms is not rolled back (forward fix).', 1;
        END;
        """;

    internal const string InvoiceLineGuardWithKilogramsSql = """
                ALTER TRIGGER [sales].[TR_InvoiceLines_Guard]
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
                    IF EXISTS (SELECT [CompanyId], [InvoiceId], [LineNo], [LineId], [ShipmentId], [DispatchNoteId], [DispatchNoteNumber], [NoteLineNo], [ShipmentDate], [SalesOrderId], [SalesOrderLineId], [PriceBasisRevision], [ProductId], [UnitId], [Quantity], [QuantityText], [UnitPrice], [PriceUnitId], [DiscountPercent], [PricingJson], [TaxJson], [NetUnitPrice], [VatPercent], [WithholdingShare], [NetAmount], [VatAmount], [WithholdingAmount], [FunctionalNetAmount], [FunctionalVatAmount], [FunctionalWithholdingAmount], [FunctionalAdjusted], [FunctionalNeedsReview], [TheoreticalKg], [ActualKg] FROM deleted EXCEPT SELECT [CompanyId], [InvoiceId], [LineNo], [LineId], [ShipmentId], [DispatchNoteId], [DispatchNoteNumber], [NoteLineNo], [ShipmentDate], [SalesOrderId], [SalesOrderLineId], [PriceBasisRevision], [ProductId], [UnitId], [Quantity], [QuantityText], [UnitPrice], [PriceUnitId], [DiscountPercent], [PricingJson], [TaxJson], [NetUnitPrice], [VatPercent], [WithholdingShare], [NetAmount], [VatAmount], [WithholdingAmount], [FunctionalNetAmount], [FunctionalVatAmount], [FunctionalWithholdingAmount], [FunctionalAdjusted], [FunctionalNeedsReview], [TheoreticalKg], [ActualKg] FROM inserted)
                        THROW 51224, 'A sales invoice line is immutable but for its release stamp.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId] AND i.[LineNo] = d.[LineNo]
                               WHERE d.[ReleasedAtUtc] IS NOT NULL AND (i.[ReleasedAtUtc] IS NULL OR i.[ReleasedAtUtc] <> d.[ReleasedAtUtc]))
                        THROW 51224, 'A released sales invoice line stays released.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId] AND i.[LineNo] = d.[LineNo]
                               WHERE d.[ReleasedAtUtc] IS NULL AND i.[ReleasedAtUtc] IS NOT NULL
                                 AND NOT EXISTS (SELECT 1 FROM [sales].[Invoices] v WHERE v.[CompanyId] = i.[CompanyId] AND v.[Id] = i.[InvoiceId] AND v.[State] IN (4, 5, 7)))
                        THROW 51224, 'A share is released only by a rejected or cancelled invoice.', 1;
                END;
                """;

    internal const string InvoiceLineGuardPreviousSql = """
                ALTER TRIGGER [sales].[TR_InvoiceLines_Guard]
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
                                 AND NOT EXISTS (SELECT 1 FROM [sales].[Invoices] v WHERE v.[CompanyId] = i.[CompanyId] AND v.[Id] = i.[InvoiceId] AND v.[State] IN (4, 5, 7)))
                        THROW 51224, 'A share is released only by a rejected or cancelled invoice.', 1;
                END;
                """;
}
