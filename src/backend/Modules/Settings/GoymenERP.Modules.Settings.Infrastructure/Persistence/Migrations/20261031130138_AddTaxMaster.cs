using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxCategories",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCategories", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SettingsTaxCategories_Actors", "LEN(LTRIM(RTRIM([CreatedBy]))) > 0 AND LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxCategories_Code", "LEN([Code]) BETWEEN 1 AND 32 AND [Code] NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_SettingsTaxCategories_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxCategories_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxCategories_Kind", "[Kind] IN ('VatClass', 'WithholdingClass', 'BuyerCategory')");
                    table.CheckConstraint("CK_SettingsTaxCategories_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxCategories_Version", "[Version] > 0 AND [RecordVersion] >= 0");
                });

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
                name: "TaxRateRecords",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Applies = table.Column<bool>(type: "bit", nullable: false),
                    RatePercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    WithholdingCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateRecords", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SettingsTaxRateRecords_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxRateRecords_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxRateRecords_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxRateRecords_Shape", "([Kind] = 'Vat' AND [BuyerCategoryId] IS NULL AND [Applies] = 1 AND [RatePercent] IS NOT NULL AND [RatePercent] >= 0 AND [RatePercent] <= 100 AND [WithholdingCode] IS NULL) OR ([Kind] = 'Withholding' AND [BuyerCategoryId] IS NOT NULL AND [BuyerCategoryId] <> [ClassId] AND (([Applies] = 1 AND [RatePercent] IS NOT NULL AND [RatePercent] > 0 AND [RatePercent] <= 100 AND [WithholdingCode] IS NOT NULL AND LEN([WithholdingCode]) BETWEEN 1 AND 16 AND [WithholdingCode] NOT LIKE '%[^A-Z0-9]%') OR ([Applies] = 0 AND [RatePercent] IS NULL AND [WithholdingCode] IS NULL)))");
                    table.CheckConstraint("CK_SettingsTaxRateRecords_Version", "[RecordVersion] > 0");
                    table.ForeignKey(
                        name: "FK_TaxRateRecords_TaxCategories_CompanyId_BuyerCategoryId",
                        columns: x => new { x.CompanyId, x.BuyerCategoryId },
                        principalSchema: "settings",
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRateRecords_TaxCategories_CompanyId_ClassId",
                        columns: x => new { x.CompanyId, x.ClassId },
                        principalSchema: "settings",
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
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
                name: "TaxRateRecordWithdrawals",
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
                    table.PrimaryKey("PK_TaxRateRecordWithdrawals", x => new { x.CompanyId, x.RecordId });
                    table.CheckConstraint("CK_SettingsTaxRateRecordWithdrawals_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxRateRecordWithdrawals_Recorded", "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxRateRecordWithdrawals_Replacement", "[ReplacedByRecordId] IS NULL OR [ReplacedByRecordId] <> [RecordId]");
                    table.ForeignKey(
                        name: "FK_TaxRateRecordWithdrawals_TaxRateRecords_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "settings",
                        principalTable: "TaxRateRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRateRecordWithdrawals_TaxRateRecords_CompanyId_ReplacedByRecordId",
                        columns: x => new { x.CompanyId, x.ReplacedByRecordId },
                        principalSchema: "settings",
                        principalTable: "TaxRateRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
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

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_CompanyId_Kind_Code",
                schema: "settings",
                table: "TaxCategories",
                columns: new[] { "CompanyId", "Kind", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_CompanyId_Kind_Name",
                schema: "settings",
                table: "TaxCategories",
                columns: new[] { "CompanyId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxCodeListWithdrawals_CompanyId_Kind_ReplacedByListVersion",
                schema: "settings",
                table: "TaxCodeListWithdrawals",
                columns: new[] { "CompanyId", "Kind", "ReplacedByListVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateRecords_CompanyId_BuyerCategoryId",
                schema: "settings",
                table: "TaxRateRecords",
                columns: new[] { "CompanyId", "BuyerCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateRecords_CompanyId_ClassId_RecordVersion",
                schema: "settings",
                table: "TaxRateRecords",
                columns: new[] { "CompanyId", "ClassId", "RecordVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateRecordWithdrawals_CompanyId_ReplacedByRecordId",
                schema: "settings",
                table: "TaxRateRecordWithdrawals",
                columns: new[] { "CompanyId", "ReplacedByRecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // MOD-03 tax master: a tax category, a rate record or a code list version is tax data an authorized person entered
            // (with its audit trail); dropping it would silently turn every resolved VAT/withholding back into "kaynak yok".
            // While any category or code list exists the rollback is refused with this migration's own error (51131, used by
            // no other migration on main or an open branch) before any operation; a rate record or list version cannot exist
            // without them. On an empty store the tables drop freely. Forward fix otherwise.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[TaxCategories])
                    OR EXISTS (SELECT 1 FROM [settings].[TaxCodeLists])
                    THROW 51131, 'Settings tax master records exist; AddTaxMaster is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "TaxCodeListEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCodeListWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxRateRecordWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCodeListVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxRateRecords",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCodeLists",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxCategories",
                schema: "settings");
        }
    }
}
