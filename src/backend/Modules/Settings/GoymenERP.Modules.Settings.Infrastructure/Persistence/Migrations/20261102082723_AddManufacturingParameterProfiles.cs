using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingParameterProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MassBalanceDifferenceTolerancePercent",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManufacturingParameterProfiles",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Process = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreparationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublicationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingParameterProfiles", x => new { x.CompanyId, x.ProfileId });
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_Lifecycle", "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND (([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Published' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedBy] <> [PreparedBy] AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0))");
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_Process", "[Process] IN ('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')");
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_Profile", "[ProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_Versions", "[ProfileVersion] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_Window", "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_SettingsMfgParameterProfiles_WorkCenter", "[WorkCenterId] IS NULL OR [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingToleranceProfileVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Process = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    MaximumFirePercent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    MassBalanceDifferenceTolerancePercent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreparationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublicationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingToleranceProfileVersions", x => new { x.CompanyId, x.ProfileId });
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Lifecycle", "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND (([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Published' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedBy] <> [PreparedBy] AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0))");
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Percents", "[MaximumFirePercent] >= 0 AND [MaximumFirePercent] <= 100 AND [MassBalanceDifferenceTolerancePercent] >= 0 AND [MassBalanceDifferenceTolerancePercent] <= 100");
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Process", "[Process] IN ('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')");
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Profile", "[ProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Versions", "[ProfileVersion] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_SettingsMfgToleranceVersions_Window", "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingParameterProfileValues",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParameterKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Unit = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    NumericValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ChoiceValue = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingParameterProfileValues", x => new { x.CompanyId, x.ProfileId, x.ParameterKey });
                    table.CheckConstraint("CK_SettingsMfgParameterValues_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgParameterValues_Key", "LEN([ParameterKey]) BETWEEN 1 AND 64 AND [ParameterKey] NOT LIKE '%[^A-Za-z0-9]%'");
                    table.CheckConstraint("CK_SettingsMfgParameterValues_Shape", "([NumericValue] IS NOT NULL AND [ChoiceValue] IS NULL) OR ([NumericValue] IS NULL AND [ChoiceValue] IS NOT NULL AND LEN([ChoiceValue]) > 0)");
                    table.CheckConstraint("CK_SettingsMfgParameterValues_Unit", "LEN(LTRIM(RTRIM([Unit]))) > 0");
                    table.ForeignKey(
                        name: "FK_ManufacturingParameterProfileValues_ManufacturingParameterProfiles_CompanyId_ProfileId",
                        columns: x => new { x.CompanyId, x.ProfileId },
                        principalSchema: "settings",
                        principalTable: "ManufacturingParameterProfiles",
                        principalColumns: new[] { "CompanyId", "ProfileId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgTolerance_MassBalance",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                sql: "[MassBalanceDifferenceTolerancePercent] IS NULL OR ([MassBalanceDifferenceTolerancePercent] >= 0 AND [MassBalanceDifferenceTolerancePercent] <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingParameterProfiles_CompanyId_Process_WorkCenterId_ProfileVersion",
                schema: "settings",
                table: "ManufacturingParameterProfiles",
                columns: new[] { "CompanyId", "Process", "WorkCenterId", "ProfileVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingParameterProfiles_CompanyId_Process_WorkCenterId_Status_EffectiveFrom",
                schema: "settings",
                table: "ManufacturingParameterProfiles",
                columns: new[] { "CompanyId", "Process", "WorkCenterId", "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingToleranceProfileVersions_CompanyId_Process_ProfileVersion",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions",
                columns: new[] { "CompanyId", "Process", "ProfileVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufacturingParameterProfileValues",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "ManufacturingToleranceProfileVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "ManufacturingParameterProfiles",
                schema: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgTolerance_MassBalance",
                schema: "settings",
                table: "ManufacturingTolerancePolicies");

            migrationBuilder.DropColumn(
                name: "MassBalanceDifferenceTolerancePercent",
                schema: "settings",
                table: "ManufacturingTolerancePolicies");
        }
    }
}
