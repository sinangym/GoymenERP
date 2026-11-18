using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

// SAT-D96: migration-only SQL, fixed owner tables, no runtime SQL surface. K3 review pending.
internal static class SalesOfficialInvoiceCancellationGuards
{
    internal const string DownSql = """
        IF EXISTS (SELECT 1 FROM [sales].[OfficialInvoiceCancellations])
            OR EXISTS (SELECT 1 FROM [sales].[OfficialInvoiceCancellationAttempts])
            OR EXISTS (SELECT 1 FROM [sales].[OfficialInvoiceCancellationDeliveries])
            OR EXISTS (SELECT 1 FROM [sales].[Invoices] WHERE [State] = 7)
            OR EXISTS (SELECT 1 FROM [sales].[SubcontractServiceInvoices] WHERE [State] = 7)
        BEGIN
            THROW 51919, 'Official cancellation evidence or immutable attempts exist; AddSalesOfficialInvoiceCancellation is not rolled back (forward fix).', 1;
        END;
        DROP TRIGGER [sales].[TR_OfficialInvoiceCancellations_Immutable];
        DROP TRIGGER [sales].[TR_OfficialInvoiceCancellationAttempts_Immutable];
        DROP TRIGGER [sales].[TR_OfficialInvoiceCancellationDeliveries_Immutable];
        EXEC(N'ALTER TRIGGER [sales].[TR_Invoices_Guard]
        ON [sales].[Invoices]
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS (SELECT 1 FROM inserted)
            BEGIN
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51223, ''Sales invoices are never deleted; cancel or reject them instead.'', 1;
                RETURN;
            END;
            IF EXISTS (SELECT 1 FROM deleted WHERE [State] IN (4, 5, 6))
                THROW 51223, ''A rejected, cancelled or finalized sales invoice is immutable.'', 1;
            -- API-36: the only move out of the approved state is the finalisation (3 -> 6): exactly one version up, the decision
            -- facts untouched, and the official match recorded before the invoice is final (SAT-D101 / S7).
            IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[Id] = d.[Id]
                       WHERE d.[State] = 3 AND (i.[State] <> 6 OR i.[Version] <> d.[Version] + 1
                          OR EXISTS (SELECT i.[SubmittedBy], i.[SubmittedAtUtc], i.[DecidedBy], i.[DecidedAtUtc]
                                     EXCEPT SELECT d.[SubmittedBy], d.[SubmittedAtUtc], d.[DecidedBy], d.[DecidedAtUtc])))
                THROW 51223, ''An approved sales invoice only moves to finalized, one version up, with its decision untouched.'', 1;
            IF EXISTS (SELECT 1 FROM inserted i WHERE i.[State] = 6
                       AND NOT EXISTS (SELECT 1 FROM [sales].[InvoiceMatches] m WHERE m.[CompanyId] = i.[CompanyId] AND m.[InvoiceId] = i.[Id]))
                THROW 51223, ''A sales invoice is finalized only after its official match is recorded.'', 1;
            IF EXISTS (SELECT [Id], [CompanyId], [DocumentNo], [BusinessDate], [PartyId], [CompanyAccountId], [CurrencyId], [MoneyProfileId], [MoneyProfileVersion], [AmountScale], [ExchangeRate], [RateDay], [RateBulletinDate], [FunctionalCurrencyId], [FunctionalScale], [InstalmentsJson], [CreatedBy], [CreatedAtUtc], [CorrelationId] FROM deleted EXCEPT SELECT [Id], [CompanyId], [DocumentNo], [BusinessDate], [PartyId], [CompanyAccountId], [CurrencyId], [MoneyProfileId], [MoneyProfileVersion], [AmountScale], [ExchangeRate], [RateDay], [RateBulletinDate], [FunctionalCurrencyId], [FunctionalScale], [InstalmentsJson], [CreatedBy], [CreatedAtUtc], [CorrelationId] FROM inserted)
                THROW 51223, ''The identity, amount and rate facts of a sales invoice are immutable.'', 1;
            -- AT-055 / SAT-D94: a rejected or cancelled invoice releases its shares in the SAME statement that decides it. EF writes
            -- independent tables in table-name order (the lines before the header), so the release stamp is the header''s, not the app''s.
            IF EXISTS (SELECT 1 FROM inserted WHERE [State] IN (4, 5))
                UPDATE l SET l.[ReleasedAtUtc] = COALESCE(i.[CancelledAtUtc], i.[DecidedAtUtc])
                FROM [sales].[InvoiceLines] l
                JOIN inserted i ON i.[CompanyId] = l.[CompanyId] AND i.[Id] = l.[InvoiceId]
                WHERE i.[State] IN (4, 5) AND l.[ReleasedAtUtc] IS NULL;
        END;');
        EXEC(N'ALTER TRIGGER [sales].[TR_InvoiceLines_Guard]
        ON [sales].[InvoiceLines]
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS (SELECT 1 FROM inserted)
            BEGIN
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51224, ''Sales invoice lines are never deleted; the share is released instead.'', 1;
                RETURN;
            END;
            IF EXISTS (SELECT [CompanyId], [InvoiceId], [LineNo], [LineId], [ShipmentId], [DispatchNoteId], [DispatchNoteNumber], [NoteLineNo], [ShipmentDate], [SalesOrderId], [SalesOrderLineId], [PriceBasisRevision], [ProductId], [UnitId], [Quantity], [QuantityText], [UnitPrice], [PriceUnitId], [DiscountPercent], [PricingJson], [TaxJson], [NetUnitPrice], [VatPercent], [WithholdingShare], [NetAmount], [VatAmount], [WithholdingAmount], [FunctionalNetAmount], [FunctionalVatAmount], [FunctionalWithholdingAmount], [FunctionalAdjusted], [FunctionalNeedsReview] FROM deleted EXCEPT SELECT [CompanyId], [InvoiceId], [LineNo], [LineId], [ShipmentId], [DispatchNoteId], [DispatchNoteNumber], [NoteLineNo], [ShipmentDate], [SalesOrderId], [SalesOrderLineId], [PriceBasisRevision], [ProductId], [UnitId], [Quantity], [QuantityText], [UnitPrice], [PriceUnitId], [DiscountPercent], [PricingJson], [TaxJson], [NetUnitPrice], [VatPercent], [WithholdingShare], [NetAmount], [VatAmount], [WithholdingAmount], [FunctionalNetAmount], [FunctionalVatAmount], [FunctionalWithholdingAmount], [FunctionalAdjusted], [FunctionalNeedsReview] FROM inserted)
                THROW 51224, ''A sales invoice line is immutable but for its release stamp.'', 1;
            IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId] AND i.[LineNo] = d.[LineNo]
                       WHERE d.[ReleasedAtUtc] IS NOT NULL AND (i.[ReleasedAtUtc] IS NULL OR i.[ReleasedAtUtc] <> d.[ReleasedAtUtc]))
                THROW 51224, ''A released sales invoice line stays released.'', 1;
            IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId] AND i.[LineNo] = d.[LineNo]
                       WHERE d.[ReleasedAtUtc] IS NULL AND i.[ReleasedAtUtc] IS NOT NULL
                         AND NOT EXISTS (SELECT 1 FROM [sales].[Invoices] v WHERE v.[CompanyId] = i.[CompanyId] AND v.[Id] = i.[InvoiceId] AND v.[State] IN (4, 5)))
                THROW 51224, ''A share is released only by a rejected or cancelled invoice.'', 1;
        END;');
        EXEC(N'ALTER TRIGGER [sales].[TR_SubcontractServiceInvoices_Lifecycle]
        ON [sales].[SubcontractServiceInvoices] AFTER UPDATE, DELETE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS (
                SELECT [Id], [CompanyId], [DocumentNo] COLLATE Latin1_General_100_BIN2, [BusinessDate], [PartyId], [CompanyAccountId],
                       [CurrencyId], [MoneyProfileId], [MoneyProfileVersion] COLLATE Latin1_General_100_BIN2, [AmountScale], [SalesOrderId],
                       [LoadingInstructionId], [WeighingId], [WeighingRevision], [InstructionVersion],
                       [NetKgText] COLLATE Latin1_General_100_BIN2, [CreatedBy] COLLATE Latin1_General_100_BIN2,
                       [CreatedAtUtc], [CorrelationId] COLLATE Latin1_General_100_BIN2 FROM deleted
                EXCEPT
                SELECT [Id], [CompanyId], [DocumentNo] COLLATE Latin1_General_100_BIN2, [BusinessDate], [PartyId], [CompanyAccountId],
                       [CurrencyId], [MoneyProfileId], [MoneyProfileVersion] COLLATE Latin1_General_100_BIN2, [AmountScale], [SalesOrderId],
                       [LoadingInstructionId], [WeighingId], [WeighingRevision], [InstructionVersion],
                       [NetKgText] COLLATE Latin1_General_100_BIN2, [CreatedBy] COLLATE Latin1_General_100_BIN2,
                       [CreatedAtUtc], [CorrelationId] COLLATE Latin1_General_100_BIN2 FROM inserted)
                OR EXISTS (
                    SELECT 1 FROM deleted d INNER JOIN inserted i ON i.[Id] = d.[Id] AND i.[CompanyId] = d.[CompanyId]
                    WHERE i.[Version] <> d.[Version] + 1 OR NOT (
                        (d.[State] = 1 AND i.[State] IN (2,5)) OR
                        (d.[State] = 2 AND i.[State] IN (3,4,5)) OR
                        (d.[State] = 3 AND i.[State] IN (3,6))))
                THROW 51997, ''Subcontract invoice facts are immutable and lifecycle transitions advance exactly once.'', 1;
        END;');
        EXEC(N'ALTER TRIGGER [sales].[TR_InvoiceFinalizations_Guard]
        ON [sales].[InvoiceFinalizations]
        AFTER UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            IF NOT EXISTS (SELECT 1 FROM inserted)
            BEGIN
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51232, ''A sales invoice finalisation is never deleted.'', 1;
                RETURN;
            END;
            IF EXISTS (SELECT [CompanyId], [InvoiceId], [AdmissionId], [FinalizedVersion], [FinalizedBy], [FinalizedAtUtc], [PlanJson], [CorrelationId] FROM deleted
                       EXCEPT SELECT [CompanyId], [InvoiceId], [AdmissionId], [FinalizedVersion], [FinalizedBy], [FinalizedAtUtc], [PlanJson], [CorrelationId] FROM inserted)
                THROW 51232, ''The finalisation facts and the instalment plan of a sales invoice are immutable.'', 1;
            IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId]
                       WHERE i.[FinanceStatus] < d.[FinanceStatus]
                          OR (d.[FinanceStatus] = 1 AND i.[AttemptCount] <> d.[AttemptCount] + 1))
                THROW 51232, ''The Finance hand-off ledger only moves forward: one attempt at a time, and an admitted invoice stays admitted.'', 1;
            -- An admitted ledger row is frozen whole: status, admission stamp, attempt count, last attempt and last outcome (EXCEPT is null-safe).
            IF EXISTS (SELECT 1 FROM deleted d WHERE d.[FinanceStatus] = 2 AND EXISTS (
                           SELECT d.[FinanceStatus], d.[AdmittedAtUtc], d.[AttemptCount], d.[LastAttemptAtUtc], d.[LastOutcome]
                           EXCEPT SELECT i.[FinanceStatus], i.[AdmittedAtUtc], i.[AttemptCount], i.[LastAttemptAtUtc], i.[LastOutcome]
                                  FROM inserted i WHERE i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId]))
                THROW 51232, ''An admitted sales invoice finalisation ledger row is frozen.'', 1;
        END;');
        """;

    internal static void Add(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TRIGGER [sales].[TR_OfficialInvoiceCancellations_Immutable]
            ON [sales].[OfficialInvoiceCancellations] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51916, 'Official invoice cancellation history is append-only.', 1;
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER [sales].[TR_OfficialInvoiceCancellationAttempts_Immutable]
            ON [sales].[OfficialInvoiceCancellationAttempts] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51917, 'Official invoice cancellation history is append-only.', 1;
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER [sales].[TR_OfficialInvoiceCancellationDeliveries_Immutable]
            ON [sales].[OfficialInvoiceCancellationDeliveries] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51918, 'Official invoice cancellation history is append-only.', 1;
            END;
            """);
        migrationBuilder.Sql("""
                ALTER TRIGGER [sales].[TR_Invoices_Guard]
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
                    IF EXISTS (SELECT 1 FROM deleted WHERE [State] IN (4, 5, 7))
                        THROW 51223, 'A rejected, cancelled or finalized sales invoice is immutable.', 1;
                    -- API-36: the only move out of the approved state is the finalisation (3 -> 6): exactly one version up, the decision
                    -- facts untouched, and the official match recorded before the invoice is final (SAT-D101 / S7).
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[Id] = d.[Id]
                               WHERE d.[State] = 3 AND (i.[State] <> 6 OR i.[Version] <> d.[Version] + 1
                                  OR EXISTS (SELECT i.[SubmittedBy], i.[SubmittedAtUtc], i.[DecidedBy], i.[DecidedAtUtc]
                                             EXCEPT SELECT d.[SubmittedBy], d.[SubmittedAtUtc], d.[DecidedBy], d.[DecidedAtUtc])))
                        THROW 51223, 'An approved sales invoice only moves to finalized, one version up, with its decision untouched.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[Id] = d.[Id]
                        WHERE d.[State] = 6 AND (i.[State] <> 7 OR i.[Version] <> d.[Version] + 1
                            OR EXISTS (SELECT i.[SubmittedBy], i.[SubmittedAtUtc], i.[DecidedBy], i.[DecidedAtUtc], i.[DecisionReason]
                                       EXCEPT SELECT d.[SubmittedBy], d.[SubmittedAtUtc], d.[DecidedBy], d.[DecidedAtUtc], d.[DecisionReason])
                            OR NOT EXISTS (SELECT 1 FROM [sales].[OfficialInvoiceCancellations] c
                                WHERE c.[CompanyId] = i.[CompanyId] AND c.[NormalInvoiceId] = i.[Id] AND c.[Kind] = 1
                                  AND c.[FinalizedVersion] = d.[Version] AND c.[InvoiceVersion] = i.[Version]
                                  AND c.[ActorId] COLLATE Latin1_General_100_BIN2 = i.[CancelledBy] COLLATE Latin1_General_100_BIN2 AND c.[RecordedAtUtc] = i.[CancelledAtUtc]
                                  AND JSON_VALUE(c.[ViewJson], '$.reason') = i.[CancellationReason])))
                        THROW 51223, 'A finalized invoice only moves to officially cancelled with its immutable exact cancellation fact.', 1;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.[CompanyId] = d.[CompanyId] AND i.[Id] = d.[Id]
                        WHERE i.[State] = 7 AND d.[State] <> 6)
                        THROW 51223, 'Official cancellation only follows finalization.', 1;
                    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[State] = 6
                               AND NOT EXISTS (SELECT 1 FROM [sales].[InvoiceMatches] m WHERE m.[CompanyId] = i.[CompanyId] AND m.[InvoiceId] = i.[Id]))
                        THROW 51223, 'A sales invoice is finalized only after its official match is recorded.', 1;
                    IF EXISTS (SELECT [Id], [CompanyId], [DocumentNo], [BusinessDate], [PartyId], [CompanyAccountId], [CurrencyId], [MoneyProfileId], [MoneyProfileVersion], [AmountScale], [ExchangeRate], [RateDay], [RateBulletinDate], [FunctionalCurrencyId], [FunctionalScale], [InstalmentsJson], [CreatedBy], [CreatedAtUtc], [CorrelationId] FROM deleted EXCEPT SELECT [Id], [CompanyId], [DocumentNo], [BusinessDate], [PartyId], [CompanyAccountId], [CurrencyId], [MoneyProfileId], [MoneyProfileVersion], [AmountScale], [ExchangeRate], [RateDay], [RateBulletinDate], [FunctionalCurrencyId], [FunctionalScale], [InstalmentsJson], [CreatedBy], [CreatedAtUtc], [CorrelationId] FROM inserted)
                        THROW 51223, 'The identity, amount and rate facts of a sales invoice are immutable.', 1;
                    -- AT-055 / SAT-D94: a rejected or cancelled invoice releases its shares in the SAME statement that decides it. EF writes
                    -- independent tables in table-name order (the lines before the header), so the release stamp is the header's, not the app's.
                    IF EXISTS (SELECT 1 FROM inserted WHERE [State] IN (4, 5, 7))
                        UPDATE l SET l.[ReleasedAtUtc] = COALESCE(i.[CancelledAtUtc], i.[DecidedAtUtc])
                        FROM [sales].[InvoiceLines] l
                        JOIN inserted i ON i.[CompanyId] = l.[CompanyId] AND i.[Id] = l.[InvoiceId]
                        WHERE i.[State] IN (4, 5, 7) AND l.[ReleasedAtUtc] IS NULL;
                END;
                """);
        migrationBuilder.Sql("""
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
                """);
        migrationBuilder.Sql("""
                ALTER TRIGGER [sales].[TR_SubcontractServiceInvoices_Lifecycle]
                ON [sales].[SubcontractServiceInvoices] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT [Id], [CompanyId], [DocumentNo] COLLATE Latin1_General_100_BIN2, [BusinessDate], [PartyId], [CompanyAccountId],
                               [CurrencyId], [MoneyProfileId], [MoneyProfileVersion] COLLATE Latin1_General_100_BIN2, [AmountScale], [SalesOrderId],
                               [LoadingInstructionId], [WeighingId], [WeighingRevision], [InstructionVersion],
                               [NetKgText] COLLATE Latin1_General_100_BIN2, [CreatedBy] COLLATE Latin1_General_100_BIN2,
                               [CreatedAtUtc], [CorrelationId] COLLATE Latin1_General_100_BIN2 FROM deleted
                        EXCEPT
                        SELECT [Id], [CompanyId], [DocumentNo] COLLATE Latin1_General_100_BIN2, [BusinessDate], [PartyId], [CompanyAccountId],
                               [CurrencyId], [MoneyProfileId], [MoneyProfileVersion] COLLATE Latin1_General_100_BIN2, [AmountScale], [SalesOrderId],
                               [LoadingInstructionId], [WeighingId], [WeighingRevision], [InstructionVersion],
                               [NetKgText] COLLATE Latin1_General_100_BIN2, [CreatedBy] COLLATE Latin1_General_100_BIN2,
                               [CreatedAtUtc], [CorrelationId] COLLATE Latin1_General_100_BIN2 FROM inserted)
                        OR EXISTS (
                            SELECT 1 FROM deleted d INNER JOIN inserted i ON i.[Id] = d.[Id] AND i.[CompanyId] = d.[CompanyId]
                            WHERE i.[Version] <> d.[Version] + 1 OR NOT (
                                (d.[State] = 1 AND i.[State] IN (2,5)) OR
                                (d.[State] = 2 AND i.[State] IN (3,4,5)) OR
                                (d.[State] = 3 AND i.[State] IN (3,6)) OR
                                (d.[State] = 6 AND i.[State] = 7 AND EXISTS (
                                    SELECT 1 FROM [sales].[OfficialInvoiceCancellations] c WHERE c.[CompanyId] = i.[CompanyId]
                                    AND c.[SubcontractInvoiceId] = i.[Id] AND c.[Kind] = 2
                                    AND c.[FinalizedVersion] = d.[Version] AND c.[InvoiceVersion] = i.[Version]))))
                        THROW 51997, 'Subcontract invoice facts are immutable and lifecycle transitions advance exactly once.', 1;
                END;
                """);
        migrationBuilder.Sql("""
                ALTER TRIGGER [sales].[TR_InvoiceFinalizations_Guard]
                ON [sales].[InvoiceFinalizations]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF NOT EXISTS (SELECT 1 FROM inserted)
                    BEGIN
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51232, 'A sales invoice finalisation is never deleted.', 1;
                        RETURN;
                    END;
                    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (
                        SELECT 1 FROM [sales].[Invoices] i WHERE i.[CompanyId] = d.[CompanyId] AND i.[Id] = d.[InvoiceId] AND i.[State] = 6))
                        THROW 51232, 'A cancelled invoice retains its original Finance delivery snapshot.', 1;
                    IF EXISTS (SELECT [CompanyId], [InvoiceId], [AdmissionId], [FinalizedVersion], [FinalizedBy], [FinalizedAtUtc], [PlanJson], [CorrelationId] FROM deleted
                               EXCEPT SELECT [CompanyId], [InvoiceId], [AdmissionId], [FinalizedVersion], [FinalizedBy], [FinalizedAtUtc], [PlanJson], [CorrelationId] FROM inserted)
                        THROW 51232, 'The finalisation facts and the instalment plan of a sales invoice are immutable.', 1;
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId]
                               WHERE i.[FinanceStatus] < d.[FinanceStatus]
                                  OR (d.[FinanceStatus] = 1 AND i.[AttemptCount] <> d.[AttemptCount] + 1))
                        THROW 51232, 'The Finance hand-off ledger only moves forward: one attempt at a time, and an admitted invoice stays admitted.', 1;
                    -- An admitted ledger row is frozen whole: status, admission stamp, attempt count, last attempt and last outcome (EXCEPT is null-safe).
                    IF EXISTS (SELECT 1 FROM deleted d WHERE d.[FinanceStatus] = 2 AND EXISTS (
                                   SELECT d.[FinanceStatus], d.[AdmittedAtUtc], d.[AttemptCount], d.[LastAttemptAtUtc], d.[LastOutcome]
                                   EXCEPT SELECT i.[FinanceStatus], i.[AdmittedAtUtc], i.[AttemptCount], i.[LastAttemptAtUtc], i.[LastOutcome]
                                          FROM inserted i WHERE i.[CompanyId] = d.[CompanyId] AND i.[InvoiceId] = d.[InvoiceId]))
                        THROW 51232, 'An admitted sales invoice finalisation ledger row is frozen.', 1;
                END;
                """);
    }
}
