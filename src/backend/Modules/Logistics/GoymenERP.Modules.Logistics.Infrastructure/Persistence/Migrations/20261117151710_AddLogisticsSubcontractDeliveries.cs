using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsSubcontractDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShipmentKind",
                schema: "logistics",
                table: "LoadingInstructions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SubcontractSourceJson",
                schema: "logistics",
                table: "LoadingInstructionLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                schema: "logistics",
                table: "LoadingAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VehicleWeighingCorrections",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    FirstWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SecondWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PreviousNetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LoadedKgAtWeighing = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DifferenceKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SharesBefore = table.Column<int>(type: "int", nullable: false),
                    CorrectedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleWeighingCorrections", x => new { x.CompanyId, x.InstructionId, x.Revision });
                    table.CheckConstraint("CK_VehicleWeighingCorrections_Difference", "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
                    table.CheckConstraint("CK_VehicleWeighingCorrections_Identity", "[CorrectionId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrectedBy]) > 0 AND LEN([Reason]) > 0");
                    table.CheckConstraint("CK_VehicleWeighingCorrections_Net", "([Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]) OR ([Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg])");
                    table.CheckConstraint("CK_VehicleWeighingCorrections_Weights", "[Revision] >= 2 AND [SharesBefore] >= 1 AND [FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetKg] > 0 AND [PreviousNetKg] > 0 AND [LoadedKgAtWeighing] >= 0");
                    table.ForeignKey(
                        name: "FK_VehicleWeighingCorrections_VehicleWeighings_CompanyId_InstructionId",
                        columns: x => new { x.CompanyId, x.InstructionId },
                        principalSchema: "logistics",
                        principalTable: "VehicleWeighings",
                        principalColumns: new[] { "CompanyId", "InstructionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructions_ShipmentKind",
                schema: "logistics",
                table: "LoadingInstructions",
                sql: "[ShipmentKind] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructionLines_SubcontractSource",
                schema: "logistics",
                table: "LoadingInstructionLines",
                sql: "[SubcontractSourceJson] IS NULL OR ISJSON([SubcontractSourceJson]) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingAttempts_ResultJson",
                schema: "logistics",
                table: "LoadingAttempts",
                sql: "[ResultJson] IS NULL OR ([Outcome] = 1 AND ISJSON([ResultJson]) = 1)");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleWeighingCorrections_CompanyId_CorrectionId",
                schema: "logistics",
                table: "VehicleWeighingCorrections",
                columns: new[] { "CompanyId", "CorrectionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[VehicleWeighingCorrections])
                   OR EXISTS (SELECT 1 FROM [logistics].[LoadingInstructions] WHERE [ShipmentKind] = 2)
                   OR EXISTS (SELECT 1 FROM [logistics].[LoadingInstructionLines] WHERE [SubcontractSourceJson] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [logistics].[LoadingAttempts] WHERE [ResultJson] IS NOT NULL)
                BEGIN
                    THROW 51935, 'Subcontract delivery, correction or replay evidence exists; AddLogisticsSubcontractDeliveries is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "VehicleWeighingCorrections",
                schema: "logistics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructions_ShipmentKind",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructionLines_SubcontractSource",
                schema: "logistics",
                table: "LoadingInstructionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingAttempts_ResultJson",
                schema: "logistics",
                table: "LoadingAttempts");

            migrationBuilder.DropColumn(
                name: "ShipmentKind",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.DropColumn(
                name: "SubcontractSourceJson",
                schema: "logistics",
                table: "LoadingInstructionLines");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                schema: "logistics",
                table: "LoadingAttempts");
        }
    }
}
