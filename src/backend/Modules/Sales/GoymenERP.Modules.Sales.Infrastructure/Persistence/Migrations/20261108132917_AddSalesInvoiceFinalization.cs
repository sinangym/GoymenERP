using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// API-36 PR4: invoice finalisation (approved -> finalized), the Finance hand-off ledger and the Sales outbox. Hand-edited SQL:
    /// TR_Invoices_Guard (3 -> 6 only with a match), TR_InvoiceFinalizations_Guard (51232), TR_OutboxMessages_Guard (51233).
    /// Down is a forward fix guarded by 51231.
    /// </summary>
    public partial class AddSalesInvoiceFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Decision",
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

            migrationBuilder.CreateTable(
                name: "InvoiceFinalizations",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinanceStatus = table.Column<int>(type: "int", nullable: false),
                    AdmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastOutcome = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceFinalizations", x => new { x.CompanyId, x.InvoiceId });
                    table.CheckConstraint("CK_InvoiceFinalizations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] >= 2 AND LEN([FinalizedBy]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([PlanJson]) = 1 AND [FinanceStatus] IN (1, 2) AND ([LastOutcome] IS NULL OR LEN([LastOutcome]) > 0)");
                    table.CheckConstraint("CK_InvoiceFinalizations_Ledger", "[AttemptCount] >= 0 AND (([AttemptCount] = 0) = ([LastAttemptAtUtc] IS NULL)) AND (([FinanceStatus] = 1 AND [AdmittedAtUtc] IS NULL) OR ([FinanceStatus] = 2 AND [AdmittedAtUtc] IS NOT NULL AND [AttemptCount] >= 1))");
                    table.ForeignKey(
                        name: "FK_InvoiceFinalizations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceFinalizations_Invoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SourceOperation = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_Envelope", "LEN([EventType]) > 0 AND [SchemaVersion] >= 1 AND LEN([SourceOperation]) > 0 AND LEN([AggregateType]) > 0 AND [AggregateVersion] >= 1 AND [Sequence] >= 1 AND LEN([CorrelationId]) > 0 AND ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_OutboxMessages_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AggregateId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices",
                sql: "(([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND [SubmittedAtUtc] IS NOT NULL)) AND (([State] = 1 AND [SubmittedBy] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] IN (3, 6) AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 5 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices",
                sql: "[State] IN (1, 2, 3, 4, 5, 6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts",
                sql: "[CommandName] IN (N'sales.invoice.create.v1', N'sales.invoice.submit.v1', N'sales.invoice.approve.v1', N'sales.invoice.reject.v1', N'sales.invoice.cancel.v1', N'sales.invoice.match.v1', N'sales.invoice.finalize.v1', N'sales.invoice.finance-resend.v1')");

            migrationBuilder.CreateIndex(
                name: "UX_InvoiceFinalizations_AdmissionId",
                schema: "sales",
                table: "InvoiceFinalizations",
                columns: new[] { "CompanyId", "AdmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OutboxMessages_AggregateSequence",
                schema: "sales",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "EventType", "AggregateId", "Sequence" },
                unique: true);

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
                    IF EXISTS (SELECT 1 FROM deleted WHERE [State] IN (4, 5, 6))
                        THROW 51223, 'A rejected, cancelled or finalized sales invoice is immutable.', 1;
                    -- API-36: the only move out of the approved state is the finalisation (3 -> 6): exactly one version up, the decision
                    -- facts untouched, and the official match recorded before the invoice is final (SAT-D101 / S7).
                    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.[CompanyId] = d.[CompanyId] AND i.[Id] = d.[Id]
                               WHERE d.[State] = 3 AND (i.[State] <> 6 OR i.[Version] <> d.[Version] + 1
                                  OR ISNULL(i.[SubmittedBy], N'') <> ISNULL(d.[SubmittedBy], N'') OR NOT (i.[SubmittedAtUtc] = d.[SubmittedAtUtc] OR (i.[SubmittedAtUtc] IS NULL AND d.[SubmittedAtUtc] IS NULL))
                                  OR ISNULL(i.[DecidedBy], N'') <> ISNULL(d.[DecidedBy], N'') OR NOT (i.[DecidedAtUtc] = d.[DecidedAtUtc] OR (i.[DecidedAtUtc] IS NULL AND d.[DecidedAtUtc] IS NULL))))
                        THROW 51223, 'An approved sales invoice only moves to finalized, one version up, with its decision untouched.', 1;
                    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[State] = 6
                               AND NOT EXISTS (SELECT 1 FROM [sales].[InvoiceMatches] m WHERE m.[CompanyId] = i.[CompanyId] AND m.[InvoiceId] = i.[Id]))
                        THROW 51223, 'A sales invoice is finalized only after its official match is recorded.', 1;
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
                CREATE TRIGGER [sales].[TR_InvoiceFinalizations_Guard]
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

            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_OutboxMessages_Guard]
                ON [sales].[OutboxMessages]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51233, 'Sales outbox messages are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a finalized invoice, a ledger row or an outbox message cannot be held by the previous model. The guard
            // refuses (51231) before anything is changed; then the new triggers go and the previous invoice guard is restored.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[Invoices] WHERE [State] = 6)
                   OR EXISTS (SELECT 1 FROM [sales].[InvoiceFinalizations])
                   OR EXISTS (SELECT 1 FROM [sales].[OutboxMessages])
                   OR EXISTS (SELECT 1 FROM [sales].[InvoiceAttempts] WHERE [CommandName] IN (N'sales.invoice.finalize.v1', N'sales.invoice.finance-resend.v1'))
                BEGIN
                    THROW 51231, 'Finalized sales invoices, ledger rows, outbox messages or their attempts exist; forward-fix instead of rolling back.', 1;
                END;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER [sales].[TR_InvoiceFinalizations_Guard];
                DROP TRIGGER [sales].[TR_OutboxMessages_Guard];
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

            migrationBuilder.DropTable(
                name: "InvoiceFinalizations",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Decision",
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices",
                sql: "(([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND [SubmittedAtUtc] IS NOT NULL)) AND (([State] = 1 AND [SubmittedBy] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 3 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 5 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices",
                sql: "[State] IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts",
                sql: "[CommandName] IN (N'sales.invoice.create.v1', N'sales.invoice.submit.v1', N'sales.invoice.approve.v1', N'sales.invoice.reject.v1', N'sales.invoice.cancel.v1', N'sales.invoice.match.v1')");
        }
    }
}
