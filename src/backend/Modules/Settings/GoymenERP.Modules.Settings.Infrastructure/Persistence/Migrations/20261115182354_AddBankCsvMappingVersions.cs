using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankCsvMappingVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankCsvMappingVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankKey = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Delimiter = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    SkipLeadingRows = table.Column<int>(type: "int", nullable: false),
                    HasHeaderRow = table.Column<bool>(type: "bit", nullable: false),
                    DecimalSeparator = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ThousandsSeparator = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DateFormat = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AmountLayout = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SignedAmountPositiveMeans = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    TransactionDateColumn = table.Column<int>(type: "int", nullable: false),
                    ValueDateColumn = table.Column<int>(type: "int", nullable: true),
                    DescriptionColumn = table.Column<int>(type: "int", nullable: false),
                    AmountColumn = table.Column<int>(type: "int", nullable: true),
                    InflowColumn = table.Column<int>(type: "int", nullable: true),
                    OutflowColumn = table.Column<int>(type: "int", nullable: true),
                    BalanceColumn = table.Column<int>(type: "int", nullable: true),
                    ReferenceColumn = table.Column<int>(type: "int", nullable: true),
                    CurrencyColumn = table.Column<int>(type: "int", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankCsvMappingVersions", x => new { x.CompanyId, x.BankKey, x.Version });
                    table.CheckConstraint("CK_SettingsBankCsvMappingVersions_Codes", "[State] IN ('Active','Inactive') AND [Delimiter] IN ('Semicolon','Comma','Tab') AND [DecimalSeparator] IN ('Comma','Period') AND [ThousandsSeparator] IN ('None','Period','Comma','Space') AND [ThousandsSeparator] <> [DecimalSeparator] AND [SkipLeadingRows] BETWEEN 0 AND 50");
                    table.CheckConstraint("CK_SettingsBankCsvMappingVersions_Columns", "[TransactionDateColumn] BETWEEN 1 AND 256 AND [DescriptionColumn] BETWEEN 1 AND 256 AND ([ValueDateColumn] IS NULL OR [ValueDateColumn] BETWEEN 1 AND 256) AND ([AmountColumn] IS NULL OR [AmountColumn] BETWEEN 1 AND 256) AND ([InflowColumn] IS NULL OR [InflowColumn] BETWEEN 1 AND 256) AND ([OutflowColumn] IS NULL OR [OutflowColumn] BETWEEN 1 AND 256) AND ([BalanceColumn] IS NULL OR [BalanceColumn] BETWEEN 1 AND 256) AND ([ReferenceColumn] IS NULL OR [ReferenceColumn] BETWEEN 1 AND 256) AND ([CurrencyColumn] IS NULL OR [CurrencyColumn] BETWEEN 1 AND 256)");
                    table.CheckConstraint("CK_SettingsBankCsvMappingVersions_DateFormat", "[DateFormat] IN ('dd.MM.yyyy','dd/MM/yyyy','dd-MM-yyyy','yyyy-MM-dd','yyyyMMdd','MM/dd/yyyy','dd.MM.yyyy HH:mm','dd.MM.yyyy HH:mm:ss','yyyy-MM-dd HH:mm:ss')");
                    table.CheckConstraint("CK_SettingsBankCsvMappingVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND LEN([BankKey]) BETWEEN 1 AND 32 AND [BankKey] LIKE '[A-Z0-9]%' AND [BankKey] NOT LIKE '%[^A-Z0-9_-]%' AND LEN(LTRIM(RTRIM([BankDisplayName]))) > 0");
                    table.CheckConstraint("CK_SettingsBankCsvMappingVersions_Layout", "([AmountLayout] = 'SignedAmount' AND [AmountColumn] IS NOT NULL AND [InflowColumn] IS NULL AND [OutflowColumn] IS NULL AND [SignedAmountPositiveMeans] IN ('Inflow','Outflow')) OR ([AmountLayout] = 'InflowOutflowColumns' AND [AmountColumn] IS NULL AND [InflowColumn] IS NOT NULL AND [OutflowColumn] IS NOT NULL AND [SignedAmountPositiveMeans] IS NULL)");
                    table.CheckConstraint("CK_SettingsBankCsvMappingVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankCsvMappingVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "BankCsvMappingVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once recorded, a bank's CSV column mapping is the only source the statement import reads (FNS-D52);
            // dropping it would silently leave every bank without a mapping. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[BankCsvMappingVersions])
                BEGIN
                    THROW 51527, 'Bank CSV mapping versions exist; AddBankCsvMappingVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "BankCsvMappingVersions",
                schema: "settings");
        }
    }
}
