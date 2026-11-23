using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnloadWeighings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnloadWeighings",
                schema: "logistics",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeighingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    FirstWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SecondWeighingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LoadedKgAtCancel = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DifferenceKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnloadWeighings", x => new { x.CompanyId, x.InstructionId });
                    table.CheckConstraint("CK_UnloadWeighings_Difference", "[DifferenceKg] = [NetKg] - [LoadedKgAtCancel]");
                    table.CheckConstraint("CK_UnloadWeighings_Direction", "[Direction] IN (1, 2)");
                    table.CheckConstraint("CK_UnloadWeighings_Identity", "[WeighingId] <> '00000000-0000-0000-0000-000000000000' AND LEN([RecordedBy]) > 0");
                    table.CheckConstraint("CK_UnloadWeighings_Net", "([Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]) OR ([Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg])");
                    table.CheckConstraint("CK_UnloadWeighings_Weights", "[FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetKg] > 0 AND [LoadedKgAtCancel] > 0");
                    table.ForeignKey(
                        name: "FK_UnloadWeighings_LoadingInstructions_CompanyId_InstructionId",
                        columns: x => new { x.CompanyId, x.InstructionId },
                        principalSchema: "logistics",
                        principalTable: "LoadingInstructions",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_UnloadWeighings_WeighingId",
                schema: "logistics",
                table: "UnloadWeighings",
                columns: new[] { "CompanyId", "WeighingId" },
                unique: true);
        }

        /// <summary>
        /// Forward fix: a recorded unload weighing (LOJ-D17) is a physical fact with its audit record; dropping the table would
        /// lose it, so Down refuses while any row exists and only drops an empty table.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [logistics].[UnloadWeighings]) "
                + "THROW 51266, 'Logistics unload weighings exist; AddUnloadWeighings is not rolled back (forward fix).', 1;");

            migrationBuilder.DropTable(
                name: "UnloadWeighings",
                schema: "logistics");
        }
    }
}
