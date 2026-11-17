using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcontractInvoiceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_SubcontractServiceInvoices_Weighing",
                schema: "sales",
                table: "SubcontractServiceInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices");

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceClaims",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ettn = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OfficialInvoiceNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceClaims", x => new { x.CompanyId, x.Ettn });
                    table.CheckConstraint("CK_OfficialInvoiceClaims_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [SourceKind] IN (N'dispatch',N'subcontract') AND LEN([OfficialInvoiceNo]) > 0 AND LEN([Ettn]) = 36");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceClaims_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubcontractInvoiceCommandAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalStatus = table.Column<int>(type: "int", nullable: true),
                    RefusalCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RefusalErrorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractInvoiceCommandAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_SubcontractInvoiceCommandAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_SubcontractInvoiceCommandAttempts_Outcome", "([RefusalStatus] IS NULL AND [RefusalCode] IS NULL AND [RefusalErrorsJson] IS NULL AND [InvoiceId] IS NOT NULL) OR ([RefusalStatus] IS NOT NULL AND [RefusalStatus] IN (400,403,404,409,422) AND [RefusalCode] IS NOT NULL AND LEN([RefusalCode]) > 0 AND [RefusalErrorsJson] IS NOT NULL AND ISJSON([RefusalErrorsJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_SubcontractInvoiceCommandAttempts_SubcontractServiceInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "SubcontractServiceInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubcontractInvoiceLifecycles",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    FactsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractInvoiceLifecycles", x => new { x.CompanyId, x.InvoiceId, x.Sequence });
                    table.CheckConstraint("CK_SubcontractInvoiceLifecycles_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [Sequence] >= 1 AND [InvoiceVersion] >= 1 AND [State] IN (1,2,3,4,5,6) AND ISJSON([FactsJson]) = 1 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_SubcontractInvoiceLifecycles_SubcontractServiceInvoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "SubcontractServiceInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SubcontractServiceInvoices_Weighing",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                columns: new[] { "CompanyId", "LoadingInstructionId", "WeighingId", "SalesOrderId" },
                unique: true,
                filter: "[State] IN (1,2,3,6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                sql: "[State] IN (1,2,3,4,5,6)");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceClaims_CompanyId_InvoiceId_SourceKind",
                schema: "sales",
                table: "OfficialInvoiceClaims",
                columns: new[] { "CompanyId", "InvoiceId", "SourceKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceClaims_CompanyId_OfficialInvoiceNo",
                schema: "sales",
                table: "OfficialInvoiceClaims",
                columns: new[] { "CompanyId", "OfficialInvoiceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubcontractInvoiceCommandAttempts_CompanyId_InvoiceId",
                schema: "sales",
                table: "SubcontractInvoiceCommandAttempts",
                columns: new[] { "CompanyId", "InvoiceId" });

            // Preserve the already-claimed official identity; never manufacture a new invoice or due plan.
            migrationBuilder.Sql("""
                INSERT INTO [sales].[OfficialInvoiceClaims] ([CompanyId], [InvoiceId], [SourceKind], [OfficialInvoiceNo], [Ettn])
                SELECT [CompanyId], [InvoiceId], N'dispatch', [OfficialInvoiceNo], [Ettn] FROM [sales].[InvoiceMatches];
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_SubcontractInvoiceLifecycles_Immutable]
                ON [sales].[SubcontractInvoiceLifecycles] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51997, 'Subcontract invoice lifecycle evidence is append-only.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_OfficialInvoiceClaims_Immutable]
                ON [sales].[OfficialInvoiceClaims] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51997, 'Official invoice identity claims are append-only.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_SubcontractInvoiceCommandAttempts_Immutable]
                ON [sales].[SubcontractInvoiceCommandAttempts] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51997, 'Subcontract invoice command attempts are append-only.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_SubcontractServiceInvoices_Lifecycle]
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
                        THROW 51997, 'Subcontract invoice facts are immutable and lifecycle transitions advance exactly once.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[SubcontractInvoiceLifecycles])
                    OR EXISTS (SELECT 1 FROM [sales].[SubcontractInvoiceCommandAttempts])
                    OR EXISTS (SELECT 1 FROM [sales].[SubcontractServiceInvoices] WHERE [State] <> 1)
                    OR EXISTS (SELECT 1 FROM [sales].[OfficialInvoiceClaims] c WHERE c.[SourceKind] <> N'dispatch'
                        OR NOT EXISTS (SELECT 1 FROM [sales].[InvoiceMatches] m WHERE m.[CompanyId] = c.[CompanyId]
                            AND m.[InvoiceId] = c.[InvoiceId] AND m.[OfficialInvoiceNo] = c.[OfficialInvoiceNo] AND m.[Ettn] = c.[Ettn]))
                BEGIN
                    THROW 51994, 'Subcontract invoice lifecycle evidence exists; AddSubcontractInvoiceLifecycle is not rolled back (forward fix).', 1;
                END;
                DROP TRIGGER [sales].[TR_SubcontractServiceInvoices_Lifecycle];
                DROP TRIGGER [sales].[TR_SubcontractInvoiceLifecycles_Immutable];
                DROP TRIGGER [sales].[TR_SubcontractInvoiceCommandAttempts_Immutable];
                DROP TRIGGER [sales].[TR_OfficialInvoiceClaims_Immutable];
                """);
            migrationBuilder.DropTable(
                name: "OfficialInvoiceClaims",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SubcontractInvoiceCommandAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "SubcontractInvoiceLifecycles",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "UX_SubcontractServiceInvoices_Weighing",
                schema: "sales",
                table: "SubcontractServiceInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices");

            migrationBuilder.CreateIndex(
                name: "UX_SubcontractServiceInvoices_Weighing",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                columns: new[] { "CompanyId", "LoadingInstructionId", "WeighingId", "SalesOrderId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                sql: "[State] = 1");
        }
    }
}
