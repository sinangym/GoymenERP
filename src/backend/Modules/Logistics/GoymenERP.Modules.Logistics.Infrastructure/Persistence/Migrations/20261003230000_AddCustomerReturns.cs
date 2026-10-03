using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;

public partial class AddCustomerReturns : Migration
{
    private const string Schema = LogisticsDbContext.Schema;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerReturns", schema: Schema,
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CustomerReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LoadingInstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LoadingInstructionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceDeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                QuantityUnit = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                SourceDeliveredQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DecisionNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                InventoryMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OriginalLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OriginalShipmentMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerReturns", x => new { x.CompanyId, x.CustomerReturnId });
                table.CheckConstraint("CK_CustomerReturns_Actors", "LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0");
                table.CheckConstraint("CK_CustomerReturns_Identity", "[CustomerReturnId] <> '00000000-0000-0000-0000-000000000000' AND [LoadingInstructionId] <> '00000000-0000-0000-0000-000000000000' AND [LoadingInstructionLineId] <> '00000000-0000-0000-0000-000000000000' AND [SourceDeliveryId] <> '00000000-0000-0000-0000-000000000000' AND [SourceShipmentId] <> '00000000-0000-0000-0000-000000000000' AND [SourceOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SourceOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CustomerReturns_Quantity", "[QuantityUnit] IN (1, 2) AND [Quantity] > 0 AND [SourceDeliveredQuantity] > 0 AND [Quantity] <= [SourceDeliveredQuantity]");
                table.CheckConstraint("CK_CustomerReturns_State", "([Status] = 1 AND [Version] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL) OR ([Status] = 2 AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [InventoryMovementId] IS NOT NULL AND [OriginalLabelId] IS NOT NULL AND [OriginalShipmentMovementId] IS NOT NULL) OR ([Status] = 3 AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL)");
                table.ForeignKey(
                    name: "FK_CustomerReturns_LoadingInstructions_CompanyId_LoadingInstructionId",
                    columns: x => new { x.CompanyId, x.LoadingInstructionId },
                    principalSchema: Schema,
                    principalTable: "LoadingInstructions",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_CustomerReturns_DeliveryStatus", schema: Schema, table: "CustomerReturns",
            columns: new[] { "CompanyId", "SourceDeliveryId", "Status", "CustomerReturnId" });
        migrationBuilder.CreateIndex(name: "IX_CustomerReturns_SourceOrderLine", schema: Schema, table: "CustomerReturns",
            columns: new[] { "CompanyId", "SourceOrderId", "SourceOrderLineId" });

        migrationBuilder.CreateTable(
            name: "CustomerReturnAttempts", schema: Schema,
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: LogisticsDbContext.IdentityCollation),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: LogisticsDbContext.IdentityCollation),
                RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CustomerReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerReturnAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_CustomerReturnAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                table.CheckConstraint("CK_CustomerReturnAttempts_Identity", "[CustomerReturnId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_CustomerReturnAttempts_Result", "ISJSON([ResultJson]) = 1");
                table.ForeignKey(
                    name: "FK_CustomerReturnAttempts_CustomerReturns_CompanyId_CustomerReturnId",
                    columns: x => new { x.CompanyId, x.CustomerReturnId },
                    principalSchema: Schema,
                    principalTable: "CustomerReturns",
                    principalColumns: new[] { "CompanyId", "CustomerReturnId" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_CustomerReturnAttempts_CompanyId_CustomerReturnId", schema: Schema,
            table: "CustomerReturnAttempts", columns: new[] { "CompanyId", "CustomerReturnId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [logistics].[CustomerReturnAttempts]) OR EXISTS (SELECT 1 FROM [logistics].[CustomerReturns])
                THROW 51057, 'Logistics customer-return history exists; AddCustomerReturns is not rolled back (forward fix).', 1;
            """);
        migrationBuilder.DropTable(name: "CustomerReturnAttempts", schema: Schema);
        migrationBuilder.DropTable(name: "CustomerReturns", schema: Schema);
    }
}
