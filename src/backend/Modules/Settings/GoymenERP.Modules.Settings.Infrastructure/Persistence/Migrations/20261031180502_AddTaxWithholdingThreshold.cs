using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MOD-03 / SAT-D93 (Sinan, 2026-10-31): the dated VUK 232 withholding amount threshold, which starts EMPTY. <c>Down</c>
    /// refuses (<c>THROW 51141</c>, used by no other migration on main or any remote branch at the time of writing) while any
    /// threshold exists — dropping it would silently turn an entered threshold back into "kaynak yok" (forward fix); on an empty
    /// store the tables drop freely.
    /// </summary>
    public partial class AddTaxWithholdingThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxWithholdingThresholds",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastRecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxWithholdingThresholds", x => x.CompanyId);
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholds_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholds_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholds_Version", "[Version] > 0 AND [LastRecordVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "TaxWithholdingThresholdRecords",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxWithholdingThresholdRecords", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Currency", "LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdRecords_Version", "[RecordVersion] > 0");
                    table.ForeignKey(
                        name: "FK_TaxWithholdingThresholdRecords_TaxWithholdingThresholds_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "settings",
                        principalTable: "TaxWithholdingThresholds",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxWithholdingThresholdWithdrawals",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplacedByRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxWithholdingThresholdWithdrawals", x => new { x.CompanyId, x.RecordId });
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdWithdrawals_Recorded", "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxWithholdingThresholdWithdrawals_Replacement", "[ReplacedByRecordId] IS NULL OR [ReplacedByRecordId] <> [RecordId]");
                    table.ForeignKey(
                        name: "FK_TaxWithholdingThresholdWithdrawals_TaxWithholdingThresholdRecords_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "settings",
                        principalTable: "TaxWithholdingThresholdRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxWithholdingThresholdWithdrawals_TaxWithholdingThresholdRecords_CompanyId_ReplacedByRecordId",
                        columns: x => new { x.CompanyId, x.ReplacedByRecordId },
                        principalSchema: "settings",
                        principalTable: "TaxWithholdingThresholdRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxWithholdingThresholdRecords_CompanyId_RecordVersion",
                schema: "settings",
                table: "TaxWithholdingThresholdRecords",
                columns: new[] { "CompanyId", "RecordVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxWithholdingThresholdWithdrawals_CompanyId_ReplacedByRecordId",
                schema: "settings",
                table: "TaxWithholdingThresholdWithdrawals",
                columns: new[] { "CompanyId", "ReplacedByRecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[TaxWithholdingThresholds])
                    THROW 51141, 'Settings withholding thresholds exist; AddTaxWithholdingThreshold is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "TaxWithholdingThresholdWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxWithholdingThresholdRecords",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxWithholdingThresholds",
                schema: "settings");
        }
    }
}
