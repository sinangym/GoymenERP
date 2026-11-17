using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceJournalSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceJournalSources",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    OwnerProfileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OwnerProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceJournalSources", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_FinanceJournalSources_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_FinanceJournalSources_Payload", "ISJSON([SourceJson]) = 1 AND LEN([SourceHash]) = 64");
                    table.CheckConstraint("CK_FinanceJournalSources_Source", "[SourceKind] IN (N'finance.expense-payment.recorded.v1', N'finance.cash-transfer.recorded.v1') AND LEN([SourceVersion]) > 0 AND LEN([OwnerProfileId]) > 0 AND LEN([OwnerProfileVersion]) > 0 AND LEN([AmountText]) > 0");
                    table.ForeignKey(
                        name: "FK_FinanceJournalSources_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceJournalSources_CompanyId_SourceId_SourceKind_SourceVersion",
                schema: "accounting",
                table: "FinanceJournalSources",
                columns: new[] { "CompanyId", "SourceId", "SourceKind", "SourceVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[FinanceJournalSources])
                BEGIN
                    THROW 51989, 'Received Finance source provenance exists; AddFinanceJournalSources is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "FinanceJournalSources",
                schema: "accounting");
        }
    }
}
