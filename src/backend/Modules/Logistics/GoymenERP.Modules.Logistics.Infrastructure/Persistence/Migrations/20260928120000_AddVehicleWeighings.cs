using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;

/// <summary>
/// AT-053: the manual weighbridge record of a loading instruction's vehicle. One row per instruction (the
/// key), append-only, tenant-scoped like every Logistics table and bound to its instruction by the
/// company-leading key. SQL Server also holds the calculation's shape (WGT-001 net = gross − tare with the
/// direction deciding which weighing is the gross, the gross strictly heavier; WGT-002 difference = net −
/// loaded, signed). No tolerance column: OPEN-004 is not decided. Additive only. <c>Down</c> refuses
/// (<c>THROW 51056</c>, Logistics's module refusal) while any weighbridge record exists — a recorded fact is
/// never silently dropped (forward fix); an empty table is dropped and nothing that existed before is touched.
/// </summary>
[DbContext(typeof(LogisticsDbContext))]
[Migration("20260928120000_AddVehicleWeighings")]
public sealed class AddVehicleWeighings : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "VehicleWeighings",
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
                LoadedKgAtWeighing = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                DifferenceKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                SharesBefore = table.Column<int>(type: "int", nullable: false),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VehicleWeighings", x => new { x.CompanyId, x.InstructionId });
                table.CheckConstraint("CK_VehicleWeighings_Identity", "[WeighingId] <> " + NotEmptyGuid + " AND LEN([RecordedBy]) > 0");
                table.CheckConstraint("CK_VehicleWeighings_Direction", "[Direction] IN (1, 2)");
                table.CheckConstraint("CK_VehicleWeighings_Weights",
                    "[FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetKg] > 0 AND [LoadedKgAtWeighing] >= 0 AND [SharesBefore] >= 0");
                // AT-053 / WGT-001: the direction names the gross, the gross is strictly heavier, net = gross − tare.
                table.CheckConstraint("CK_VehicleWeighings_Net",
                    "([Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]) "
                    + "OR ([Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg])");
                // WGT-002: the signed difference; a fact, never a verdict.
                table.CheckConstraint("CK_VehicleWeighings_Difference", "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
                table.ForeignKey(
                    name: "FK_VehicleWeighings_LoadingInstructions_CompanyId_InstructionId",
                    columns: x => new { x.CompanyId, x.InstructionId },
                    principalSchema: "logistics",
                    principalTable: "LoadingInstructions",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_VehicleWeighings_WeighingId",
            schema: "logistics",
            table: "VehicleWeighings",
            columns: new[] { "CompanyId", "WeighingId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A weighbridge record is an append-only fact of the instruction (it moved the instruction's version and is replayed
        // by the rebuild), so dropping the table would silently erase it. While any record exists the rollback is refused,
        // before the drop, with Logistics's own module error (51056, used by no other module on main or any branch at the
        // time of writing); an empty database rolls back freely. Forward fix, the Finance 51051 / Sales 51054 pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [logistics].[VehicleWeighings])
                THROW 51056, 'Logistics weighbridge records exist; AddVehicleWeighings is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(name: "VehicleWeighings", schema: "logistics");
    }
}
