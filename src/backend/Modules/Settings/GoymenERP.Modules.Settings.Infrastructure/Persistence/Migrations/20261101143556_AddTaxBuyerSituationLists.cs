using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SAT-D158 (EK21): the company's effective-dated buyer situation list — whole list versions, their entries with the tax
    /// effect as data (withholding kept or removed, VAT mode, document required) and insert-only withdrawals. Nothing is seeded
    /// (the five SAT-D140 situations are the reviewed source catalog, version 0). <c>Down</c> refuses (<c>THROW 51153</c>, used by
    /// no other migration on main or any remote branch at the time of writing) while any Settings list exists — dropping it would
    /// silently return an entered situation list to the catalog (forward fix); on an empty store the tables drop freely.
    /// </summary>
    public partial class AddTaxBuyerSituationLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxBuyerSituationLists",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastListVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxBuyerSituationLists", x => x.CompanyId);
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationLists_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationLists_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationLists_Version", "[Version] > 0 AND [LastListVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "TaxBuyerSituationListVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxBuyerSituationListVersions", x => new { x.CompanyId, x.ListVersion });
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationListVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationListVersions_Version", "[ListVersion] > 0");
                    table.ForeignKey(
                        name: "FK_TaxBuyerSituationListVersions_TaxBuyerSituationLists_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "settings",
                        principalTable: "TaxBuyerSituationLists",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxBuyerSituationEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WithholdingApplies = table.Column<bool>(type: "bit", nullable: false),
                    VatMode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DocumentRequired = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxBuyerSituationEntries", x => new { x.CompanyId, x.ListVersion, x.Code });
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationEntries_Code", "LEN([Code]) BETWEEN 1 AND 64 AND [Code] NOT LIKE '%[^A-Za-z0-9]%' AND [Code] LIKE '[A-Za-z]%'");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationEntries_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationEntries_VatMode", "[VatMode] IN ('Normal', 'ZeroWithExemptionCode')");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationEntries_ZeroNoWithholding", "[VatMode] <> 'ZeroWithExemptionCode' OR [WithholdingApplies] = 0");
                    table.ForeignKey(
                        name: "FK_TaxBuyerSituationEntries_TaxBuyerSituationListVersions_CompanyId_ListVersion",
                        columns: x => new { x.CompanyId, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxBuyerSituationListVersions",
                        principalColumns: new[] { "CompanyId", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxBuyerSituationListWithdrawals",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReplacedByListVersion = table.Column<long>(type: "bigint", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxBuyerSituationListWithdrawals", x => new { x.CompanyId, x.ListVersion });
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationListWithdrawals_Recorded", "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxBuyerSituationListWithdrawals_Replacement", "[ReplacedByListVersion] IS NULL OR [ReplacedByListVersion] > [ListVersion]");
                    table.ForeignKey(
                        name: "FK_TaxBuyerSituationListWithdrawals_TaxBuyerSituationListVersions_CompanyId_ListVersion",
                        columns: x => new { x.CompanyId, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxBuyerSituationListVersions",
                        principalColumns: new[] { "CompanyId", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxBuyerSituationListWithdrawals_TaxBuyerSituationListVersions_CompanyId_ReplacedByListVersion",
                        columns: x => new { x.CompanyId, x.ReplacedByListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxBuyerSituationListVersions",
                        principalColumns: new[] { "CompanyId", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxBuyerSituationListWithdrawals_CompanyId_ReplacedByListVersion",
                schema: "settings",
                table: "TaxBuyerSituationListWithdrawals",
                columns: new[] { "CompanyId", "ReplacedByListVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[TaxBuyerSituationLists])
                    THROW 51153, 'Settings buyer situation lists exist; AddTaxBuyerSituationLists is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "TaxBuyerSituationEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxBuyerSituationListWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxBuyerSituationListVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxBuyerSituationLists",
                schema: "settings");
        }
    }
}
