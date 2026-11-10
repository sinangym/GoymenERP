using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentRiskExceptions : Migration
    {
        // API-07 (EK21 SAT-D153…D162): the single-shipment risk exception of an approved order and its own append-only attempt
        // records. Two new tables only; no existing row changes. The request facts never change and the decision moves once
        // (TR_ShipmentRiskExceptions_Guard, 51252); the attempts are append-only (TR_ShipmentRiskExceptionAttempts_Guard, 51253).
        // Down guard THROW 51251.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentRiskExceptions",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionNo = table.Column<int>(type: "int", nullable: false),
                    LoadingInstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ValidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentRiskExceptions", x => x.Id);
                    table.UniqueConstraint("AK_ShipmentRiskExceptions_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ShipmentRiskExceptions_Decision", "([State] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NULL AND [Version] >= 1) OR ([State] = 2 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL AND [ValidOn] IS NOT NULL AND [Version] >= 2) OR ([State] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [ValidOn] IS NULL AND [Version] >= 2)");
                    table.CheckConstraint("CK_ShipmentRiskExceptions_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LoadingInstructionId] <> '00000000-0000-0000-0000-000000000000' AND [ExceptionNo] >= 1");
                    table.CheckConstraint("CK_ShipmentRiskExceptions_MakerChecker", "[DecidedBy] IS NULL OR [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [RequestedBy] COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ShipmentRiskExceptions_State", "[State] IN (1, 2, 3)");
                    table.CheckConstraint("CK_ShipmentRiskExceptions_Text", "LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptions_Drafts_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentRiskExceptionAttempts",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentRiskExceptionAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ShipmentRiskExceptionAttempts_Command", "[CommandName] IN (N'sales.order.risk-exception.request.v1', N'sales.order.risk-exception.approve.v1', N'sales.order.risk-exception.reject.v1')");
                    table.CheckConstraint("CK_ShipmentRiskExceptionAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ShipmentRiskExceptionAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_ShipmentRiskExceptionAttempts_Outcome", "([Outcome] = 1 AND [ExceptionId] IS NOT NULL AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [ExceptionId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptionAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptionAttempts_Drafts_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptionAttempts_ShipmentRiskExceptions_CompanyId_ExceptionId",
                        columns: x => new { x.CompanyId, x.ExceptionId },
                        principalSchema: "sales",
                        principalTable: "ShipmentRiskExceptions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentRiskExceptionAttempts_CompanyId_ExceptionId",
                schema: "sales",
                table: "ShipmentRiskExceptionAttempts",
                columns: new[] { "CompanyId", "ExceptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentRiskExceptionAttempts_CompanyId_OrderId",
                schema: "sales",
                table: "ShipmentRiskExceptionAttempts",
                columns: new[] { "CompanyId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentRiskExceptions_Shipment",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                columns: new[] { "CompanyId", "OrderId", "LoadingInstructionId", "ExceptionNo" });

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentRiskExceptions_No",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                columns: new[] { "CompanyId", "OrderId", "ExceptionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentRiskExceptions_PendingShipment",
                schema: "sales",
                table: "ShipmentRiskExceptions",
                columns: new[] { "CompanyId", "OrderId", "LoadingInstructionId" },
                unique: true,
                filter: "[State] = 1");

            // SAT-D153/D161: the request facts are evidence (requester, shipment, reason, time); only a Requested row takes its one
            // decision, the version moves by exactly one, and nothing is deleted (51252).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_ShipmentRiskExceptions_Guard]
                ON [sales].[ShipmentRiskExceptions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted AS d
                        LEFT JOIN inserted AS i ON i.[Id] = d.[Id]
                        WHERE i.[Id] IS NULL
                           OR d.[State] <> 1
                           OR i.[CompanyId] <> d.[CompanyId]
                           OR i.[OrderId] <> d.[OrderId]
                           OR i.[ExceptionNo] <> d.[ExceptionNo]
                           OR i.[LoadingInstructionId] <> d.[LoadingInstructionId]
                           OR i.[Reason] COLLATE Latin1_General_100_BIN2 <> d.[Reason] COLLATE Latin1_General_100_BIN2
                           OR i.[RequestedBy] COLLATE Latin1_General_100_BIN2 <> d.[RequestedBy] COLLATE Latin1_General_100_BIN2
                           OR i.[RequestedAtUtc] <> d.[RequestedAtUtc]
                           OR i.[Version] <> d.[Version] + 1)
                    BEGIN
                        THROW 51252, 'A sales shipment risk exception keeps its request facts and takes one decision only.', 1;
                    END;
                END;
                """);

            // ADR-011: the attempt records are append-only (51253).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_ShipmentRiskExceptionAttempts_Guard]
                ON [sales].[ShipmentRiskExceptionAttempts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51253, 'Sales shipment risk exception attempts are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a requested, approved or rejected exception (or a recorded attempt) is approval evidence that the previous
            // model cannot hold. The guard refuses (51251) before anything is dropped; dropping a table drops its trigger.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[ShipmentRiskExceptions])
                   OR EXISTS (SELECT 1 FROM [sales].[ShipmentRiskExceptionAttempts])
                BEGIN
                    THROW 51251, 'Sales shipment risk exceptions were recorded; AddShipmentRiskExceptions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ShipmentRiskExceptionAttempts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "ShipmentRiskExceptions",
                schema: "sales");
        }
    }
}
