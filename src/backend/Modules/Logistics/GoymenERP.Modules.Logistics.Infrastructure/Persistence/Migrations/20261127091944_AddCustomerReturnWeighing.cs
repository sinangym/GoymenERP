using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReturnWeighing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.AddColumn<decimal>(
                name: "FirstWeighingKg",
                schema: "logistics",
                table: "CustomerReturns",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeighingKg",
                schema: "logistics",
                table: "CustomerReturns",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SecondWeighingKg",
                schema: "logistics",
                table: "CustomerReturns",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeighingDirection",
                schema: "logistics",
                table: "CustomerReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WeighingId",
                schema: "logistics",
                table: "CustomerReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WeighingRecordedAtUtc",
                schema: "logistics",
                table: "CustomerReturns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeighingRecordedBy",
                schema: "logistics",
                table: "CustomerReturns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CustomerReturns_WeighingId",
                schema: "logistics",
                table: "CustomerReturns",
                columns: new[] { "CompanyId", "WeighingId" },
                unique: true,
                filter: "[WeighingId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "logistics",
                table: "CustomerReturns",
                sql: "([Status] = 1 AND [Version] = CASE WHEN [WeighingId] IS NULL THEN 1 ELSE 2 END AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL) OR ([Status] = 2 AND [Version] = CASE WHEN [WeighingId] IS NULL THEN 2 ELSE 3 END AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [InventoryMovementId] IS NOT NULL AND [OriginalLabelId] IS NOT NULL AND [OriginalShipmentMovementId] IS NOT NULL) OR ([Status] = 3 AND [Version] = CASE WHEN [WeighingId] IS NULL THEN 2 ELSE 3 END AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_Weighing",
                schema: "logistics",
                table: "CustomerReturns",
                sql: "([WeighingId] IS NULL AND [WeighingDirection] IS NULL AND [FirstWeighingKg] IS NULL AND [SecondWeighingKg] IS NULL AND [NetWeighingKg] IS NULL AND [WeighingRecordedBy] IS NULL AND [WeighingRecordedAtUtc] IS NULL) OR ([WeighingId] IS NOT NULL AND [WeighingId] <> '00000000-0000-0000-0000-000000000000' AND [QuantityUnit] = 2 AND [WeighingDirection] IS NOT NULL AND [FirstWeighingKg] IS NOT NULL AND [FirstWeighingKg] > 0 AND [SecondWeighingKg] IS NOT NULL AND [SecondWeighingKg] > 0 AND [NetWeighingKg] IS NOT NULL AND [NetWeighingKg] > 0 AND [WeighingRecordedBy] IS NOT NULL AND LEN([WeighingRecordedBy]) > 0 AND [WeighingRecordedAtUtc] IS NOT NULL AND (([WeighingDirection] = 1 AND [NetWeighingKg] = [SecondWeighingKg] - [FirstWeighingKg]) OR ([WeighingDirection] = 2 AND [NetWeighingKg] = [FirstWeighingKg] - [SecondWeighingKg])))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[CustomerReturns]
                    WHERE [WeighingId] IS NOT NULL OR [WeighingDirection] IS NOT NULL
                       OR [FirstWeighingKg] IS NOT NULL OR [SecondWeighingKg] IS NOT NULL OR [NetWeighingKg] IS NOT NULL
                       OR [WeighingRecordedBy] IS NOT NULL OR [WeighingRecordedAtUtc] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [logistics].[CustomerReturnAttempts]
                    WHERE [CommandName] = N'logistics.customer-return.record-weighing.v1')
                BEGIN
                    THROW 52353, 'Customer return weighing evidence or receipt history exists; AddCustomerReturnWeighing is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "UX_CustomerReturns_WeighingId",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomerReturns_Weighing",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "FirstWeighingKg",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "NetWeighingKg",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "SecondWeighingKg",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "WeighingDirection",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "WeighingId",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "WeighingRecordedAtUtc",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "WeighingRecordedBy",
                schema: "logistics",
                table: "CustomerReturns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomerReturns_State",
                schema: "logistics",
                table: "CustomerReturns",
                sql: "([Status] = 1 AND [Version] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL) OR ([Status] = 2 AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [InventoryMovementId] IS NOT NULL AND [OriginalLabelId] IS NOT NULL AND [OriginalShipmentMovementId] IS NOT NULL) OR ([Status] = 3 AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL)");
        }
    }
}
