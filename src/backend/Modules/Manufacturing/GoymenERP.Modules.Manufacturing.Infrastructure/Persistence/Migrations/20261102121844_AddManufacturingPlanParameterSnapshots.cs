using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanParameterSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderClosureDecisions_Identity",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.AddColumn<string>(
                name: "MassBalanceDifferenceTolerancePercent",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicySource",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PolicyWorkCenterId",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanParameterSnapshots",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileVersion = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Process = table.Column<int>(type: "int", nullable: false),
                    ProfileWorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    SelectedForDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanParameterSnapshots", x => new { x.CompanyId, x.PlanId });
                    table.CheckConstraint("CK_PlanParameterSnapshots_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Process] IN (1, 2, 3, 4, 5) AND ([ProfileWorkCenterId] IS NULL OR [ProfileWorkCenterId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_PlanParameterSnapshots_Source", "([Source] = 'Catalog' AND [ProfileVersion] = '0') OR ([Source] = 'Settings' AND LEN([ProfileVersion]) > 0 AND [ProfileVersion] NOT LIKE '%[^0-9]%' AND [ProfileVersion] LIKE '%[1-9]%')");
                    table.CheckConstraint("CK_PlanParameterSnapshots_Window", "([EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]) AND [SelectedForDate] >= [EffectiveFrom] AND ([EffectiveThrough] IS NULL OR [SelectedForDate] <= [EffectiveThrough])");
                    table.ForeignKey(
                        name: "FK_PlanParameterSnapshots_Plans_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalSchema: "manufacturing",
                        principalTable: "Plans",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanParameterSnapshotValues",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParameterKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Unit = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    NumericValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ChoiceValue = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanParameterSnapshotValues", x => new { x.CompanyId, x.PlanId, x.ParameterKey });
                    table.CheckConstraint("CK_PlanParameterSnapshotValues_Key", "LEN([ParameterKey]) BETWEEN 1 AND 64 AND [ParameterKey] NOT LIKE '%[^A-Za-z0-9]%'");
                    table.CheckConstraint("CK_PlanParameterSnapshotValues_Shape", "([NumericValue] IS NOT NULL AND [ChoiceValue] IS NULL) OR ([NumericValue] IS NULL AND [ChoiceValue] IS NOT NULL AND LEN([ChoiceValue]) > 0)");
                    table.CheckConstraint("CK_PlanParameterSnapshotValues_Unit", "LEN(LTRIM(RTRIM([Unit]))) > 0");
                    table.ForeignKey(
                        name: "FK_PlanParameterSnapshotValues_PlanParameterSnapshots_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalSchema: "manufacturing",
                        principalTable: "PlanParameterSnapshots",
                        principalColumns: new[] { "CompanyId", "PlanId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderClosureDecisions_Identity",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderVersion] >= 1 AND [PolicyVersion] >= 0 AND [Process] IN (1, 2, 3, 4, 5) AND LEN([MaximumFirePercent]) > 0 AND [IsActive] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderClosureDecisions_Scope",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                sql: "([PolicyWorkCenterId] IS NULL OR [PolicyWorkCenterId] <> '00000000-0000-0000-0000-000000000000') AND ([MassBalanceDifferenceTolerancePercent] IS NULL OR LEN([MassBalanceDifferenceTolerancePercent]) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderClosureDecisions_Source",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                sql: "([PolicySource] = 'Catalog' AND [PolicyVersion] = 0) OR (([PolicySource] IS NULL OR [PolicySource] = 'Settings') AND [PolicyVersion] >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanParameterSnapshotValues",
                schema: "manufacturing");

            migrationBuilder.DropTable(
                name: "PlanParameterSnapshots",
                schema: "manufacturing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderClosureDecisions_Identity",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderClosureDecisions_Scope",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderClosureDecisions_Source",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.DropColumn(
                name: "MassBalanceDifferenceTolerancePercent",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.DropColumn(
                name: "PolicySource",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.DropColumn(
                name: "PolicyWorkCenterId",
                schema: "manufacturing",
                table: "OrderClosureDecisions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderClosureDecisions_Identity",
                schema: "manufacturing",
                table: "OrderClosureDecisions",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderVersion] >= 1 AND [PolicyVersion] >= 1 AND [Process] IN (1, 2, 3, 4, 5) AND LEN([MaximumFirePercent]) > 0 AND [IsActive] = 1");
        }
    }
}
