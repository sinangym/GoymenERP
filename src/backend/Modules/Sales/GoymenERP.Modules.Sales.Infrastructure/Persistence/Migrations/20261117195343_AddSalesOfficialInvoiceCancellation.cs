using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOfficialInvoiceCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractInvoiceLifecycles_Identity",
                schema: "sales",
                table: "SubcontractInvoiceLifecycles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceCancellations",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    NormalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubcontractInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    InvoiceVersion = table.Column<long>(type: "bigint", nullable: false),
                    AdmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdmissionJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceCancellations", x => new { x.CompanyId, x.CancellationId });
                    table.CheckConstraint("CK_OfficialInvoiceCancellations_Identity", "[CancellationId] <> '00000000-0000-0000-0000-000000000000' AND [AdmissionId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0 AND [InvoiceVersion] = [FinalizedVersion] + 1 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialInvoiceCancellations_Json", "ISJSON([ViewJson]) = 1 AND ISJSON([AdmissionJson]) = 1");
                    table.CheckConstraint("CK_OfficialInvoiceCancellations_Source", "([Kind] = 1 AND [NormalInvoiceId] = [InvoiceId] AND [NormalInvoiceId] IS NOT NULL AND [SubcontractInvoiceId] IS NULL) OR ([Kind] = 2 AND [SubcontractInvoiceId] = [InvoiceId] AND [SubcontractInvoiceId] IS NOT NULL AND [NormalInvoiceId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceCancellations_Invoices_CompanyId_NormalInvoiceId",
                        columns: x => new { x.CompanyId, x.NormalInvoiceId },
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceCancellations_SubcontractServiceInvoices_CompanyId_SubcontractInvoiceId",
                        columns: x => new { x.CompanyId, x.SubcontractInvoiceId },
                        principalSchema: "sales",
                        principalTable: "SubcontractServiceInvoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceCancellationAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceCancellationAttempts", x => new { x.CompanyId, x.CommandName, x.KeyHash });
                    table.CheckConstraint("CK_OfficialInvoiceCancellationAttempts_Identity", "LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND [CommandName] IN (N'sales.invoice.official-cancellation.record.v1', N'sales.invoice.official-cancellation.finance-retry.v1')");
                    table.CheckConstraint("CK_OfficialInvoiceCancellationAttempts_Outcome", "([CancellationId] IS NOT NULL AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1 AND [RefusalJson] IS NULL) OR ([ResultJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceCancellationAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceCancellationAttempts_OfficialInvoiceCancellations_CompanyId_CancellationId",
                        columns: x => new { x.CompanyId, x.CancellationId },
                        principalSchema: "sales",
                        principalTable: "OfficialInvoiceCancellations",
                        principalColumns: new[] { "CompanyId", "CancellationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialInvoiceCancellationDeliveries",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleaseActiveAllocations = table.Column<bool>(type: "bit", nullable: false),
                    ViewJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialInvoiceCancellationDeliveries", x => new { x.CompanyId, x.CancellationId, x.Sequence });
                    table.CheckConstraint("CK_OfficialInvoiceCancellationDeliveries_Identity", "[Sequence] > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ViewJson]) = 1");
                    table.ForeignKey(
                        name: "FK_OfficialInvoiceCancellationDeliveries_OfficialInvoiceCancellations_CompanyId_CancellationId",
                        columns: x => new { x.CompanyId, x.CancellationId },
                        principalSchema: "sales",
                        principalTable: "OfficialInvoiceCancellations",
                        principalColumns: new[] { "CompanyId", "CancellationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                sql: "[State] IN (1,2,3,4,5,6,7)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractInvoiceLifecycles_Identity",
                schema: "sales",
                table: "SubcontractInvoiceLifecycles",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [Sequence] >= 1 AND [InvoiceVersion] >= 1 AND [State] IN (1,2,3,4,5,6,7) AND ISJSON([FactsJson]) = 1 AND LEN([CorrelationId]) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices",
                sql: "(([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND [SubmittedAtUtc] IS NOT NULL)) AND (([State] = 1 AND [SubmittedBy] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 2 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] IN (3, 6) AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 4 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL) OR ([State] = 5 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL) OR ([State] = 7 AND [SubmittedBy] IS NOT NULL AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices",
                sql: "[State] IN (1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceCancellationAttempts_CompanyId_CancellationId",
                schema: "sales",
                table: "OfficialInvoiceCancellationAttempts",
                columns: new[] { "CompanyId", "CancellationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceCancellations_CompanyId_Kind_InvoiceId",
                schema: "sales",
                table: "OfficialInvoiceCancellations",
                columns: new[] { "CompanyId", "Kind", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceCancellations_CompanyId_NormalInvoiceId",
                schema: "sales",
                table: "OfficialInvoiceCancellations",
                columns: new[] { "CompanyId", "NormalInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialInvoiceCancellations_CompanyId_SubcontractInvoiceId",
                schema: "sales",
                table: "OfficialInvoiceCancellations",
                columns: new[] { "CompanyId", "SubcontractInvoiceId" });

            SalesOfficialInvoiceCancellationGuards.Add(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SalesOfficialInvoiceCancellationGuards.DownSql);

            migrationBuilder.DropTable(
                name: "OfficialInvoiceCancellationAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "OfficialInvoiceCancellationDeliveries",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "OfficialInvoiceCancellations",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubcontractInvoiceLifecycles_Identity",
                schema: "sales",
                table: "SubcontractInvoiceLifecycles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Decision",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_State",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractServiceInvoices_State",
                schema: "sales",
                table: "SubcontractServiceInvoices",
                sql: "[State] IN (1,2,3,4,5,6)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubcontractInvoiceLifecycles_Identity",
                schema: "sales",
                table: "SubcontractInvoiceLifecycles",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [Sequence] >= 1 AND [InvoiceVersion] >= 1 AND [State] IN (1,2,3,4,5,6) AND ISJSON([FactsJson]) = 1 AND LEN([CorrelationId]) > 0");

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
        }
    }
}
