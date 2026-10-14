using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Carries Product's accepted nullable nominal length into the immutable plan and revision technical snapshots.
/// Legacy families remain null; no historical value is inferred or rewritten.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration(MigrationId)]
public sealed class AddManufacturingPlanTechnicalLengthSnapshots : Migration
{
    public const string MigrationId = "20261015123000_AddManufacturingPlanTechnicalLengthSnapshots";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TechnicalLengthMm",
                schema: ManufacturingDbContext.Schema,
                table: table,
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);
            migrationBuilder.DropCheckConstraint(
                name: Constraint(table),
                schema: ManufacturingDbContext.Schema,
                table: table);
            migrationBuilder.AddCheckConstraint(
                name: Constraint(table),
                schema: ManufacturingDbContext.Schema,
                table: table,
                sql: TechnicalSnapshotCheckWithLength);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.DropCheckConstraint(
                name: Constraint(table),
                schema: ManufacturingDbContext.Schema,
                table: table);
            migrationBuilder.DropColumn(
                name: "TechnicalLengthMm",
                schema: ManufacturingDbContext.Schema,
                table: table);
            migrationBuilder.AddCheckConstraint(
                name: Constraint(table),
                schema: ManufacturingDbContext.Schema,
                table: table,
                sql: TechnicalSnapshotCheckWithoutLength);
        }
    }

    private static readonly string[] Tables = ["PlanInputs", "PlanRevisionInputs"];

    private static string Constraint(string table) => table == "PlanInputs"
        ? "CK_PlanInputs_TechnicalSnapshot"
        : "CK_PlanRevisionInputs_TechnicalSnapshot";

    private const string EmptyPrefix =
        "([TechnicalProductId] IS NULL AND [TechnicalProductVersion] IS NULL "
        + "AND [TechnicalFamilyId] IS NULL AND [TechnicalFamilyCode] IS NULL AND [TechnicalFamilySourceRevision] IS NULL "
        + "AND [TechnicalTemplateId] IS NULL AND [TechnicalTemplateVersion] IS NULL AND [TechnicalTemplateCode] IS NULL "
        + "AND [TechnicalTemplateSourceRevision] IS NULL AND [TechnicalKilogramUnitCompanyId] IS NULL "
        + "AND [TechnicalKilogramUnitId] IS NULL AND [TechnicalKilogramUnitCode] IS NULL "
        + "AND [TechnicalKilogramProfileId] IS NULL AND [TechnicalKilogramProfileVersion] IS NULL "
        + "AND [TechnicalEffectiveOn] IS NULL AND [TechnicalThicknessMm] IS NULL AND [TechnicalWidthMm] IS NULL";

    private const string Populated =
        "([TechnicalProductId] IS NOT NULL AND [TechnicalProductId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [TechnicalProductVersion] IS NOT NULL AND LEN([TechnicalProductVersion]) > 0 "
        + "AND [TechnicalFamilyId] IS NOT NULL AND [TechnicalFamilyId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [TechnicalFamilyCode] IS NOT NULL AND LEN([TechnicalFamilyCode]) > 0 "
        + "AND [TechnicalFamilySourceRevision] IS NOT NULL AND LEN([TechnicalFamilySourceRevision]) > 0 "
        + "AND [TechnicalTemplateId] IS NOT NULL AND [TechnicalTemplateId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [TechnicalTemplateVersion] IS NOT NULL AND LEN([TechnicalTemplateVersion]) > 0 "
        + "AND [TechnicalTemplateCode] IS NOT NULL AND LEN([TechnicalTemplateCode]) > 0 "
        + "AND [TechnicalTemplateSourceRevision] IS NOT NULL AND LEN([TechnicalTemplateSourceRevision]) > 0 "
        + "AND [TechnicalKilogramUnitCompanyId] IS NOT NULL AND [TechnicalKilogramUnitCompanyId] = [CompanyId] "
        + "AND [TechnicalKilogramUnitId] IS NOT NULL AND [TechnicalKilogramUnitId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [TechnicalKilogramUnitCode] IS NOT NULL AND LEN([TechnicalKilogramUnitCode]) > 0 "
        + "AND [TechnicalKilogramProfileId] IS NOT NULL AND [TechnicalKilogramProfileId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [TechnicalKilogramProfileVersion] IS NOT NULL AND LEN([TechnicalKilogramProfileVersion]) > 0 "
        + "AND [TechnicalEffectiveOn] IS NOT NULL "
        + "AND [TechnicalThicknessMm] IS NOT NULL AND [TechnicalThicknessMm] > 0 "
        + "AND [TechnicalWidthMm] IS NOT NULL AND [TechnicalWidthMm] > 0";

    private const string TechnicalSnapshotCheckWithLength =
        EmptyPrefix + " AND [TechnicalLengthMm] IS NULL) OR "
        + Populated + " AND ([TechnicalLengthMm] IS NULL OR [TechnicalLengthMm] > 0))";

    private const string TechnicalSnapshotCheckWithoutLength = EmptyPrefix + ") OR " + Populated + ")";
}
