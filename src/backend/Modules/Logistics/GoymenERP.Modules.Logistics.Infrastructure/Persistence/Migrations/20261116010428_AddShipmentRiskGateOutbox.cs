using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentRiskGateOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "logistics",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Position = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                    table.CheckConstraint("CK_OutboxMessages_Envelope", "[EventId] <> '00000000-0000-0000-0000-000000000000' AND [SubjectId] <> '00000000-0000-0000-0000-000000000000' AND [SchemaVersion] >= 1 AND LEN([CorrelationId]) > 0 AND ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_OutboxMessages_EventType", "[EventType] IN ('logistics.definite-shipment.recorded.v1', 'logistics.definite-shipment.cancelled.v1')");
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentRiskExceptionUses",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionNo = table.Column<int>(type: "int", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentRiskExceptionUses", x => new { x.CompanyId, x.SalesOrderId, x.ExceptionNo });
                    table.CheckConstraint("CK_ShipmentRiskExceptionUses_Identity", "[ExceptionNo] >= 1 AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [InstructionId] <> '00000000-0000-0000-0000-000000000000' AND LEN([RecordedBy]) > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptionUses_LoadingInstructions_CompanyId_InstructionId",
                        columns: x => new { x.CompanyId, x.InstructionId },
                        principalSchema: "logistics",
                        principalTable: "LoadingInstructions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "logistics",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "Position" });

            migrationBuilder.CreateIndex(
                name: "UX_OutboxMessages_Subject",
                schema: "logistics",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "EventType", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentRiskExceptionUses_CompanyId_InstructionId",
                schema: "logistics",
                table: "ShipmentRiskExceptionUses",
                columns: new[] { "CompanyId", "InstructionId" });

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentRiskExceptionUses_ShipmentId",
                schema: "logistics",
                table: "ShipmentRiskExceptionUses",
                columns: new[] { "CompanyId", "ShipmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded definite-shipment event or a consumed risk exception is evidence (an undispatched event,
            // a single-use exception), so the rollback refuses while any exists; an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[OutboxMessages])
                    OR EXISTS (SELECT 1 FROM [logistics].[ShipmentRiskExceptionUses])
                BEGIN
                    THROW 51787, 'Logistics outbox events or consumed shipment risk exceptions exist; AddShipmentRiskGateOutbox is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "logistics");

            migrationBuilder.DropTable(
                name: "ShipmentRiskExceptionUses",
                schema: "logistics");
        }
    }
}
