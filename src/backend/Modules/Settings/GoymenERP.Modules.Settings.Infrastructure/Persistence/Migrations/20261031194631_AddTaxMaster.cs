using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MOD-03 tax master (SAT-D93/D133/D135/D139): the per-item-type default series and records, the UBL-TR code lists and the
    /// VUK 232 withholding threshold, each with insert-only withdrawals. Nothing is seeded (the SAT-D139 defaults are the
    /// reviewed source catalog). <c>Down</c> refuses (<c>THROW 51131</c>, used by no other migration on main or any remote
    /// branch at the time of writing) while any default series, code list or threshold exists — dropping them would silently
    /// return entered tax data to the catalog / "kaynak yok" (forward fix); on an empty store the tables drop freely.
    /// </summary>
    public partial class AddTaxMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxCodeLists",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastListVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodeLists", x => new { x.CompanyId, x.Kind });
                    table.CheckConstraint("CK_SettingsTaxCodeLists_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxCodeLists_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxCodeLists_Kind", "[Kind] IN ('Exemption', 'Withholding')");
                    table.CheckConstraint("CK_SettingsTaxCodeLists_Version", "[Version] > 0 AND [LastListVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "TaxItemTypeDefaultSeries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastRecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxItemTypeDefaultSeries", x => new { x.CompanyId, x.ItemType });
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_ItemType", "[ItemType] IN ('Steel', 'Scrap', 'Service')");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultSeries_Version", "[Version] > 0 AND [LastRecordVersion] > 0");
                });

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
                name: "TaxCodeListVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodeListVersions", x => new { x.CompanyId, x.Kind, x.ListVersion });
                    table.CheckConstraint("CK_SettingsTaxCodeListVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxCodeListVersions_Version", "[ListVersion] > 0");
                    table.ForeignKey(
                        name: "FK_TaxCodeListVersions_TaxCodeLists_CompanyId_Kind",
                        columns: x => new { x.CompanyId, x.Kind },
                        principalSchema: "settings",
                        principalTable: "TaxCodeLists",
                        principalColumns: new[] { "CompanyId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxItemTypeDefaultRecords",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    VatApplies = table.Column<bool>(type: "bit", nullable: false),
                    VatPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    ExemptionCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2"),
                    WithholdingApplies = table.Column<bool>(type: "bit", nullable: false),
                    WithholdingPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    WithholdingCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxItemTypeDefaultRecords", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Codes", "([ExemptionCode] IS NULL OR (LEN([ExemptionCode]) BETWEEN 1 AND 16 AND [ExemptionCode] NOT LIKE '%[^A-Z0-9]%')) AND ([WithholdingCode] IS NULL OR (LEN([WithholdingCode]) BETWEEN 1 AND 16 AND [WithholdingCode] NOT LIKE '%[^A-Z0-9]%'))");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Vat", "([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL)");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Version", "[RecordVersion] > 0");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultRecords_Withholding", "([WithholdingApplies] = 1 AND [VatApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TaxItemTypeDefaultRecords_TaxItemTypeDefaultSeries_CompanyId_ItemType",
                        columns: x => new { x.CompanyId, x.ItemType },
                        principalSchema: "settings",
                        principalTable: "TaxItemTypeDefaultSeries",
                        principalColumns: new[] { "CompanyId", "ItemType" },
                        onDelete: ReferentialAction.Restrict);
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
                name: "TaxCodeListEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodeListEntries", x => new { x.CompanyId, x.Kind, x.ListVersion, x.Code });
                    table.CheckConstraint("CK_SettingsTaxCodeListEntries_Code", "LEN([Code]) BETWEEN 1 AND 16 AND [Code] NOT LIKE '%[^A-Z0-9]%'");
                    table.CheckConstraint("CK_SettingsTaxCodeListEntries_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_TaxCodeListEntries_TaxCodeListVersions_CompanyId_Kind_ListVersion",
                        columns: x => new { x.CompanyId, x.Kind, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxCodeListVersions",
                        principalColumns: new[] { "CompanyId", "Kind", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxCodeListWithdrawals",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReplacedByListVersion = table.Column<long>(type: "bigint", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCodeListWithdrawals", x => new { x.CompanyId, x.Kind, x.ListVersion });
                    table.CheckConstraint("CK_SettingsTaxCodeListWithdrawals_Recorded", "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxCodeListWithdrawals_Replacement", "[ReplacedByListVersion] IS NULL OR [ReplacedByListVersion] > [ListVersion]");
                    table.ForeignKey(
                        name: "FK_TaxCodeListWithdrawals_TaxCodeListVersions_CompanyId_Kind_ListVersion",
                        columns: x => new { x.CompanyId, x.Kind, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxCodeListVersions",
                        principalColumns: new[] { "CompanyId", "Kind", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxCodeListWithdrawals_TaxCodeListVersions_CompanyId_Kind_ReplacedByListVersion",
                        columns: x => new { x.CompanyId, x.Kind, x.ReplacedByListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxCodeListVersions",
                        principalColumns: new[] { "CompanyId", "Kind", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxItemTypeDefaultWithdrawals",
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
                    table.PrimaryKey("PK_TaxItemTypeDefaultWithdrawals", x => new { x.CompanyId, x.RecordId });
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultWithdrawals_Recorded", "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxItemTypeDefaultWithdrawals_Replacement", "[ReplacedByRecordId] IS NULL OR [ReplacedByRecordId] <> [RecordId]");
                    table.ForeignKey(
                        name: "FK_TaxItemTypeDefaultWithdrawals_TaxItemTypeDefaultRecords_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "settings",
                        principalTable: "TaxItemTypeDefaultRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxItemTypeDefaultWithdrawals_TaxItemTypeDefaultRecords_CompanyId_ReplacedByRecordId",
                        columns: x => new { x.CompanyId, x.ReplacedByRecordId },
                        principalSchema: "settings",
                        principalTable: "TaxItemTypeDefaultRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
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
                name: "IX_TaxCodeListWithdrawals_CompanyId_Kind_ReplacedByListVersion",
                schema: "settings",
                table: "TaxCodeListWithdrawals",
                columns: new[] { "CompanyId", "Kind", "ReplacedByListVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxItemTypeDefaultRecords_CompanyId_ItemType_RecordVersion",
                schema: "settings",
                table: "TaxItemTypeDefaultRecords",
                columns: new[] { "CompanyId", "ItemType", "RecordVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxItemTypeDefaultWithdrawals_CompanyId_ReplacedByRecordId",
                schema: "settings",
                table: "TaxItemTypeDefaultWithdrawals",
                columns: new[] { "CompanyId", "ReplacedByRecordId" });

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
                IF EXISTS (SELECT 1 FROM [settings].[TaxItemTypeDefaultSeries])
                    OR EXISTS (SELECT 1 FROM [settings].[TaxCodeLists])
                    OR EXISTS (SELECT 1 FROM [settings].[TaxWithholdingThresholds])
                    THROW 51131, 'Settings tax master records exist; AddTaxMaster is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "TaxCodeListEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCodeListWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxItemTypeDefaultWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxWithholdingThresholdWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCodeListVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxItemTypeDefaultRecords",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxWithholdingThresholdRecords",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCodeLists",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxItemTypeDefaultSeries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxWithholdingThresholds",
                schema: "settings");
        }
    }
}
