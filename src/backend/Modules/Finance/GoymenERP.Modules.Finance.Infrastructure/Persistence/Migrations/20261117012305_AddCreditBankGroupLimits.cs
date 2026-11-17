using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditBankGroupLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditLimits",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LenderPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LenderCompanyPartyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditGroup = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AmountText = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousLimitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLimits", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CreditLimits_Amount", "LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.'");
                    table.CheckConstraint("CK_CreditLimits_Decision", "([Status] = 1 AND [Version] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] IN (2,3) AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedBy] <> [ProposedBy] AND [DecidedAtUtc] IS NOT NULL AND (([Status] = 2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND LEN([RejectionReason]) > 0)))");
                    table.CheckConstraint("CK_CreditLimits_Source", "ISJSON([SourceJson]) = 1 AND [CreditGroup] IN (1,2,3,4) AND [Revision] > 0");
                    table.ForeignKey(
                        name: "FK_CreditLimits_CreditAccounts_CompanyId_CreditAccountId",
                        columns: x => new { x.CompanyId, x.CreditAccountId },
                        principalSchema: "finance",
                        principalTable: "CreditAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLimits_CreditLimits_CompanyId_PreviousLimitId",
                        columns: x => new { x.CompanyId, x.PreviousLimitId },
                        principalSchema: "finance",
                        principalTable: "CreditLimits",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLimits_CompanyId_CreditAccountId",
                schema: "finance",
                table: "CreditLimits",
                columns: new[] { "CompanyId", "CreditAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLimits_CompanyId_LenderPartyId_LenderCompanyPartyAccountId_CreditGroup_CurrencyId",
                schema: "finance",
                table: "CreditLimits",
                columns: new[] { "CompanyId", "LenderPartyId", "LenderCompanyPartyAccountId", "CreditGroup", "CurrencyId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLimits_CompanyId_LenderPartyId_LenderCompanyPartyAccountId_CreditGroup_CurrencyId_Revision",
                schema: "finance",
                table: "CreditLimits",
                columns: new[] { "CompanyId", "LenderPartyId", "LenderCompanyPartyAccountId", "CreditGroup", "CurrencyId", "Revision" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLimits_CompanyId_PreviousLimitId",
                schema: "finance",
                table: "CreditLimits",
                columns: new[] { "CompanyId", "PreviousLimitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CreditLimits])
                    THROW 51974, 'Credit bank/group limit source history exists; preserve it with a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "CreditLimits",
                schema: "finance");
        }
    }
}
