using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCollateralTypeCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollateralTypes",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollateralTypes", x => new { x.CompanyId, x.EntryId });
                    table.CheckConstraint("CK_SettingsCollateralTypes_Code", "LEN([Code]) BETWEEN 1 AND 32 AND [Code] NOT LIKE '%[^A-Z0-9._-]%' AND LEFT([Code], 1) LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_SettingsCollateralTypes_Created", "LEN(LTRIM(RTRIM([CreatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsCollateralTypes_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsCollateralTypes_Kind", "[Kind] IN ('BankGuaranteedPayment','Dbs','GuaranteeLetter','CreditCard','Vinov','Mortgage')");
                });

            migrationBuilder.CreateTable(
                name: "CollateralTypeVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RatePercent = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollateralTypeVersions", x => new { x.CompanyId, x.EntryId, x.Version });
                    table.CheckConstraint("CK_SettingsCollateralTypeVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsCollateralTypeVersions_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_SettingsCollateralTypeVersions_RateText", "LEN([RatePercent]) BETWEEN 1 AND 64 AND [RatePercent] NOT LIKE '%[^0-9.]%' AND LEFT([RatePercent], 1) <> '.' AND RIGHT([RatePercent], 1) <> '.' AND LEN([RatePercent]) - LEN(REPLACE([RatePercent], '.', '')) <= 1 AND (TRY_CONVERT(int, LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1)) BETWEEN 0 AND 99 OR [RatePercent] = '100')");
                    table.CheckConstraint("CK_SettingsCollateralTypeVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_CollateralTypeVersions_CollateralTypes_CompanyId_EntryId",
                        columns: x => new { x.CompanyId, x.EntryId },
                        principalSchema: "settings",
                        principalTable: "CollateralTypes",
                        principalColumns: new[] { "CompanyId", "EntryId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollateralTypes_CompanyId_Code",
                schema: "settings",
                table: "CollateralTypes",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[CollateralTypes])
                   OR EXISTS (SELECT 1 FROM [settings].[CollateralTypeVersions])
                BEGIN
                    THROW 51941, 'Collateral catalog rows exist; AddCollateralTypeCatalog is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CollateralTypeVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "CollateralTypes",
                schema: "settings");
        }
    }
}
