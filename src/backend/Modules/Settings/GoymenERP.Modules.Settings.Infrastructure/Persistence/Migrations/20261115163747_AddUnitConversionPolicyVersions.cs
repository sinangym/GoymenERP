using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitConversionPolicyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnitConversionPolicyVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DensityGramsPerCubicCentimetre = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    CoefficientDecimals = table.Column<int>(type: "int", nullable: false),
                    CoefficientRounding = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    DocumentTotalDecimals = table.Column<int>(type: "int", nullable: false),
                    DocumentTotalRounding = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    WholeUnitRounding = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    WeighbridgeTolerancePercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    ToleranceBoundary = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ToleranceBase = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitConversionPolicyVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsUnitConversionPolicyVersions_Decimals", "[CoefficientDecimals] BETWEEN 0 AND 12 AND [DocumentTotalDecimals] BETWEEN 0 AND 12");
                    table.CheckConstraint("CK_SettingsUnitConversionPolicyVersions_Density", "[DensityGramsPerCubicCentimetre] > 0");
                    table.CheckConstraint("CK_SettingsUnitConversionPolicyVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsUnitConversionPolicyVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsUnitConversionPolicyVersions_Rounding", "[CoefficientRounding] IN ('ToEven','AwayFromZero','ToZero','ToNegativeInfinity','ToPositiveInfinity') AND [DocumentTotalRounding] IN ('ToEven','AwayFromZero','ToZero','ToNegativeInfinity','ToPositiveInfinity') AND [WholeUnitRounding] IN ('ToEven','AwayFromZero','ToZero','ToNegativeInfinity','ToPositiveInfinity')");
                    table.CheckConstraint("CK_SettingsUnitConversionPolicyVersions_Tolerance", "[WeighbridgeTolerancePercent] >= 0 AND [WeighbridgeTolerancePercent] <= 100 AND [ToleranceBoundary] IN ('Inclusive','Exclusive') AND [ToleranceBase] IN ('TheoreticalKg','WeighedKg')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversionPolicyVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "UnitConversionPolicyVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once recorded, the policy is the company's only source of the density, coefficient / document total
            // rounding and weighbridge tolerance (F-004; nothing is compiled or seeded); dropping it would silently leave every
            // conversion without a policy. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[UnitConversionPolicyVersions])
                BEGIN
                    THROW 51330, 'Unit conversion policy versions exist; AddUnitConversionPolicyVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "UnitConversionPolicyVersions",
                schema: "settings");
        }
    }
}
