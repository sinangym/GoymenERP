using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementCommands", x => new { x.CompanyId, x.KeyHash });
                    table.CheckConstraint("CK_BankStatementCommands_Evidence", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND [ImportId] <> '00000000-0000-0000-0000-000000000000' AND ISJSON([ResultJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "BankStatementImports",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    BankKey = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true, collation: "Latin1_General_100_BIN2"),
                    State = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentVersion = table.Column<int>(type: "int", nullable: true),
                    DocumentSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CsvMappingProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CsvMappingProfileVersion = table.Column<long>(type: "bigint", nullable: true),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    DuplicateLineCount = table.Column<int>(type: "int", nullable: false),
                    FirstTransactionOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastTransactionOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReservedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ParsedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ParsedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImports", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_BankStatementImports_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [Format] IN (1,2,3) AND [State] IN (1,2) AND LEN([ReservedBy]) > 0 AND (([Format] = 1 AND [BankKey] IS NOT NULL AND LEN([BankKey]) > 0) OR ([Format] <> 1 AND [BankKey] IS NULL))");
                    table.CheckConstraint("CK_BankStatementImports_State", "([State] = 1 AND [DocumentId] IS NULL AND [DocumentVersion] IS NULL AND [DocumentSha256] IS NULL AND [CsvMappingProfileId] IS NULL AND [CsvMappingProfileVersion] IS NULL AND [LineCount] = 0 AND [DuplicateLineCount] = 0 AND [FirstTransactionOn] IS NULL AND [LastTransactionOn] IS NULL AND [ParsedBy] IS NULL AND [ParsedAtUtc] IS NULL) OR ([State] = 2 AND [DocumentId] IS NOT NULL AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentVersion] >= 1 AND LEN([DocumentSha256]) = 64 AND [LineCount] >= 0 AND [DuplicateLineCount] >= 0 AND [LineCount] + [DuplicateLineCount] >= 1 AND [FirstTransactionOn] IS NOT NULL AND [LastTransactionOn] >= [FirstTransactionOn] AND LEN([ParsedBy]) > 0 AND [ParsedAtUtc] IS NOT NULL AND (([Format] = 1 AND [CsvMappingProfileId] IS NOT NULL AND [CsvMappingProfileVersion] >= 1) OR ([Format] <> 1 AND [CsvMappingProfileId] IS NULL AND [CsvMappingProfileVersion] IS NULL)))");
                    table.ForeignKey(
                        name: "FK_BankStatementImports_BankAccounts_CompanyId_BankAccountId",
                        columns: x => new { x.CompanyId, x.BankAccountId },
                        principalSchema: "finance",
                        principalTable: "BankAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Occurrence = table.Column<int>(type: "int", nullable: false),
                    TransactionOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ValueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(max)", nullable: false),
                    BalanceText = table.Column<string>(type: "varchar(max)", nullable: true),
                    CurrencyText = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CounterpartyIban = table.Column<string>(type: "varchar(26)", unicode: false, maxLength: 26, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_BankStatementLines_Content", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ImportId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [LineNumber] >= 0 AND [Occurrence] >= 1 AND LEN([Fingerprint]) = 64 AND [Direction] IN (1,2) AND LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.' AND LEN([Description]) > 0 AND ([CounterpartyIban] IS NULL OR LEN([CounterpartyIban]) = 26)");
                    table.ForeignKey(
                        name: "FK_BankStatementLines_BankStatementImports_CompanyId_ImportId",
                        columns: x => new { x.CompanyId, x.ImportId },
                        principalSchema: "finance",
                        principalTable: "BankStatementImports",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_CompanyId_BankAccountId_DocumentSha256",
                schema: "finance",
                table: "BankStatementImports",
                columns: new[] { "CompanyId", "BankAccountId", "DocumentSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_CompanyId_BankAccountId_ReservedAtUtc",
                schema: "finance",
                table: "BankStatementImports",
                columns: new[] { "CompanyId", "BankAccountId", "ReservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_CompanyId_BankAccountId_Fingerprint_Occurrence",
                schema: "finance",
                table: "BankStatementLines",
                columns: new[] { "CompanyId", "BankAccountId", "Fingerprint", "Occurrence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_CompanyId_ImportId_LineNumber",
                schema: "finance",
                table: "BankStatementLines",
                columns: new[] { "CompanyId", "ImportId", "LineNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP12 forward fix: an import (with its lines and command records) is control evidence of what a bank
            // statement said; dropping it would lose that record. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[BankStatementImports])
                   OR EXISTS (SELECT 1 FROM [finance].[BankStatementLines])
                   OR EXISTS (SELECT 1 FROM [finance].[BankStatementCommands])
                BEGIN
                    THROW 52250, 'Bank statement imports exist; AddBankStatementImports is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "BankStatementCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "BankStatementLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "BankStatementImports",
                schema: "finance");
        }
    }
}
