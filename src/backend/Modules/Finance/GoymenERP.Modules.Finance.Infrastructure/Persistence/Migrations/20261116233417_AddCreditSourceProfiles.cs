using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditSourceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CreditAccounts_Live",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.AddColumn<int>(
                name: "CurrencyScale",
                schema: "finance",
                table: "CreditAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LenderCompanyAccountVersion",
                schema: "finance",
                table: "CreditAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LenderCompanyPartyAccountId",
                schema: "finance",
                table: "CreditAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LenderPartyId",
                schema: "finance",
                table: "CreditAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LenderPartyVersion",
                schema: "finance",
                table: "CreditAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MoneyProfileId",
                schema: "finance",
                table: "CreditAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MoneyProfileVersion",
                schema: "finance",
                table: "CreditAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProfileJson",
                schema: "finance",
                table: "CreditAccounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChargesJson",
                schema: "finance",
                table: "CreditAccountInstallments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CreditAccounts_LenderIdentity_Live",
                schema: "finance",
                table: "CreditAccounts",
                columns: new[] { "CompanyId", "LenderPartyId", "LenderCompanyPartyAccountId", "ContractReference" },
                unique: true,
                filter: "[Status] IN (1, 2) AND [LenderPartyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CreditAccounts_Live",
                schema: "finance",
                table: "CreditAccounts",
                columns: new[] { "CompanyId", "LenderName", "ContractReference" },
                unique: true,
                filter: "[Status] IN (1, 2) AND [LenderPartyId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditAccounts_SourceProfile",
                schema: "finance",
                table: "CreditAccounts",
                sql: "([SourceProfileJson] IS NULL AND [LenderPartyId] IS NULL AND [LenderCompanyPartyAccountId] IS NULL AND [LenderPartyVersion] IS NULL AND [LenderCompanyAccountVersion] IS NULL AND [MoneyProfileId] IS NULL AND [MoneyProfileVersion] IS NULL AND [CurrencyScale] IS NULL) OR ([SourceProfileJson] IS NOT NULL AND ISJSON([SourceProfileJson]) = 1 AND [LenderPartyId] IS NOT NULL AND [LenderPartyId] <> '00000000-0000-0000-0000-000000000000' AND [LenderCompanyPartyAccountId] IS NOT NULL AND [LenderCompanyPartyAccountId] <> '00000000-0000-0000-0000-000000000000' AND [LenderPartyVersion] IS NOT NULL AND [LenderPartyVersion] > 0 AND [LenderCompanyAccountVersion] IS NOT NULL AND [LenderCompanyAccountVersion] > 0 AND [MoneyProfileId] IS NOT NULL AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileVersion] IS NOT NULL AND [MoneyProfileVersion] > 0 AND [CurrencyScale] IS NOT NULL AND [CurrencyScale] BETWEEN 0 AND 28)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditAccountInstallments_Charges",
                schema: "finance",
                table: "CreditAccountInstallments",
                sql: "[ChargesJson] IS NULL OR ISJSON([ChargesJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CreditAccounts]
                    WHERE [SourceProfileJson] IS NOT NULL OR [LenderPartyId] IS NOT NULL
                        OR [LenderCompanyPartyAccountId] IS NOT NULL OR [LenderPartyVersion] IS NOT NULL
                        OR [LenderCompanyAccountVersion] IS NOT NULL OR [MoneyProfileId] IS NOT NULL
                        OR [MoneyProfileVersion] IS NOT NULL OR [CurrencyScale] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [finance].[CreditAccountInstallments] WHERE [ChargesJson] IS NOT NULL)
                BEGIN
                    THROW 51961, 'Credit source profiles or bank charges exist; AddCreditSourceProfiles is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "UX_CreditAccounts_LenderIdentity_Live",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropIndex(
                name: "UX_CreditAccounts_Live",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditAccounts_SourceProfile",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditAccountInstallments_Charges",
                schema: "finance",
                table: "CreditAccountInstallments");

            migrationBuilder.DropColumn(
                name: "CurrencyScale",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "LenderCompanyAccountVersion",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "LenderCompanyPartyAccountId",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "LenderPartyId",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "LenderPartyVersion",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "MoneyProfileId",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "MoneyProfileVersion",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "SourceProfileJson",
                schema: "finance",
                table: "CreditAccounts");

            migrationBuilder.DropColumn(
                name: "ChargesJson",
                schema: "finance",
                table: "CreditAccountInstallments");

            migrationBuilder.CreateIndex(
                name: "UX_CreditAccounts_Live",
                schema: "finance",
                table: "CreditAccounts",
                columns: new[] { "CompanyId", "LenderName", "ContractReference" },
                unique: true,
                filter: "[Status] IN (1, 2)");
        }
    }
}
