using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CounterpartyDbContext))]
[Migration("20260912003000_AddCompanyAccountContactInformation")]
public sealed class AddCompanyAccountContactInformation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContactInformationJson", schema: "counterparty", table: "CompanyPartyAccounts",
            type: "nvarchar(max)", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_CompanyPartyAccounts_ContactInformationJson",
            schema: "counterparty", table: "CompanyPartyAccounts",
            sql: "[ContactInformationJson] IS NULL OR ISJSON([ContactInformationJson]) = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [counterparty].[CompanyPartyAccounts] WHERE [ContactInformationJson] IS NOT NULL)
            BEGIN
                THROW 51000, 'Rollback blocked: contact information exists. Preserve the schema and use a compatible forward fix.', 1;
            END;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "CK_CompanyPartyAccounts_ContactInformationJson",
            schema: "counterparty", table: "CompanyPartyAccounts");
        migrationBuilder.DropColumn(
            name: "ContactInformationJson", schema: "counterparty", table: "CompanyPartyAccounts");
    }
}
