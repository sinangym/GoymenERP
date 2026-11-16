using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentRiskExceptionUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentRiskExceptionUsages",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ExceptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExceptionNo = table.Column<int>(type: "int", nullable: false),
                    LoadingInstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentRiskExceptionUsages", x => new { x.CompanyId, x.ShipmentId, x.Kind });
                    table.CheckConstraint("CK_ShipmentRiskExceptionUsages_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [ExceptionId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LoadingInstructionId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEventId] <> '00000000-0000-0000-0000-000000000000' AND [ExceptionNo] >= 1");
                    table.CheckConstraint("CK_ShipmentRiskExceptionUsages_Kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("CK_ShipmentRiskExceptionUsages_Text", "LEN([SourceName]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptionUsages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentRiskExceptionUsages_ShipmentRiskExceptions_CompanyId_ExceptionId",
                        columns: x => new { x.CompanyId, x.ExceptionId },
                        principalSchema: "sales",
                        principalTable: "ShipmentRiskExceptions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ShipmentRiskExceptionUsages_Exception",
                schema: "sales",
                table: "ShipmentRiskExceptionUsages",
                columns: new[] { "CompanyId", "ExceptionId", "Kind" },
                unique: true);

            // LOJ-D19, SAT-D285: a mirrored usage of a single-shipment risk exception is evidence; it is never changed or removed.
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_ShipmentRiskExceptionUsages_Guard]
                ON [sales].[ShipmentRiskExceptionUsages]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51487, 'Risk exception usages mirrored from Logistics are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a mirrored usage is evidence that the previous model cannot hold (the exception would read as valid again).
            // The guard refuses (51488) before anything is dropped; dropping the table drops its trigger.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[ShipmentRiskExceptionUsages])
                BEGIN
                    THROW 51488, 'Risk exception usages were mirrored; AddShipmentRiskExceptionUsages is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ShipmentRiskExceptionUsages",
                schema: "sales");
        }
    }
}
