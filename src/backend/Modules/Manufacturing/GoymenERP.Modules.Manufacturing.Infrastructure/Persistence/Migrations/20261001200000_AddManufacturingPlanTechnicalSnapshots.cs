using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds nullable-on-legacy, all-or-none Product technical provenance to the creation input and each
/// append-only revision input. New application writes require a complete snapshot. Down uses the
/// existing Manufacturing module error number and refuses to discard any captured provenance.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20261001200000_AddManufacturingPlanTechnicalSnapshots")]
public sealed class AddManufacturingPlanTechnicalSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddColumns("PlanInputs");
        AddColumns("PlanRevisionInputs");

        void AddColumns(string table)
        {
            migrationBuilder.AddColumn<Guid>(name: "TechnicalProductId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalProductVersion", schema: "manufacturing", table: table,
                type: "varchar(128)", unicode: false, maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "TechnicalFamilyId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalFamilyCode", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalFamilySourceRevision", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "TechnicalTemplateId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalTemplateVersion", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalTemplateCode", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalTemplateSourceRevision", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "TechnicalKilogramUnitCompanyId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "TechnicalKilogramUnitId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalKilogramUnitCode", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "TechnicalKilogramProfileId", schema: "manufacturing", table: table,
                type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TechnicalKilogramProfileVersion", schema: "manufacturing", table: table,
                type: "varchar(64)", unicode: false, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "TechnicalEffectiveOn", schema: "manufacturing", table: table,
                type: "date", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "TechnicalThicknessMm", schema: "manufacturing", table: table,
                type: "decimal(18,3)", precision: 18, scale: 3, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "TechnicalWidthMm", schema: "manufacturing", table: table,
                type: "decimal(18,3)", precision: 18, scale: 3, nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: table == "PlanInputs"
                    ? "CK_PlanInputs_TechnicalSnapshot"
                    : "CK_PlanRevisionInputs_TechnicalSnapshot",
                schema: "manufacturing",
                table: table,
                sql: TechnicalSnapshotCheck);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [manufacturing].[PlanInputs] WHERE [TechnicalProductId] IS NOT NULL)
                OR EXISTS (SELECT 1 FROM [manufacturing].[PlanRevisionInputs] WHERE [TechnicalProductId] IS NOT NULL)
                THROW 51047, 'Manufacturing plan technical snapshots must be forward-fixed before downgrade.', 1;
            """);

        DropColumns("PlanRevisionInputs");
        DropColumns("PlanInputs");

        void DropColumns(string table)
        {
            migrationBuilder.DropCheckConstraint(
                name: table == "PlanInputs"
                    ? "CK_PlanInputs_TechnicalSnapshot"
                    : "CK_PlanRevisionInputs_TechnicalSnapshot",
                schema: "manufacturing",
                table: table);

            foreach (var column in Columns)
            {
                migrationBuilder.DropColumn(name: column, schema: "manufacturing", table: table);
            }
        }
    }

    private static readonly string[] Columns =
    [
        "TechnicalProductId", "TechnicalProductVersion", "TechnicalFamilyId", "TechnicalFamilyCode",
        "TechnicalFamilySourceRevision", "TechnicalTemplateId", "TechnicalTemplateVersion",
        "TechnicalTemplateCode", "TechnicalTemplateSourceRevision", "TechnicalKilogramUnitCompanyId",
        "TechnicalKilogramUnitId", "TechnicalKilogramUnitCode", "TechnicalKilogramProfileId",
        "TechnicalKilogramProfileVersion", "TechnicalEffectiveOn", "TechnicalThicknessMm", "TechnicalWidthMm",
    ];

    private const string TechnicalSnapshotCheck =
        "([TechnicalProductId] IS NULL AND [TechnicalProductVersion] IS NULL "
        + "AND [TechnicalFamilyId] IS NULL AND [TechnicalFamilyCode] IS NULL AND [TechnicalFamilySourceRevision] IS NULL "
        + "AND [TechnicalTemplateId] IS NULL AND [TechnicalTemplateVersion] IS NULL AND [TechnicalTemplateCode] IS NULL "
        + "AND [TechnicalTemplateSourceRevision] IS NULL AND [TechnicalKilogramUnitCompanyId] IS NULL "
        + "AND [TechnicalKilogramUnitId] IS NULL AND [TechnicalKilogramUnitCode] IS NULL "
        + "AND [TechnicalKilogramProfileId] IS NULL AND [TechnicalKilogramProfileVersion] IS NULL "
        + "AND [TechnicalEffectiveOn] IS NULL AND [TechnicalThicknessMm] IS NULL AND [TechnicalWidthMm] IS NULL) OR "
        + "([TechnicalProductId] IS NOT NULL AND [TechnicalProductId] <> '00000000-0000-0000-0000-000000000000' "
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
        + "AND [TechnicalEffectiveOn] IS NOT NULL AND [TechnicalThicknessMm] > 0 AND [TechnicalWidthMm] > 0)";
}
