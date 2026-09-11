using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CounterpartyDbContext))]
[Migration("20260910120000_AllowCounterpartyAccountNoRole")]
public sealed class AllowCounterpartyAccountNoRole : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CompanyPartyAccounts_Roles",
            table: "CompanyPartyAccounts",
            schema: "counterparty");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CompanyPartyAccounts_Roles",
            table: "CompanyPartyAccounts",
            schema: "counterparty",
            sql: "[Roles] BETWEEN 0 AND 15");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [counterparty].[CompanyPartyAccounts] WHERE [Roles] = 0)
            BEGIN
                THROW 51000, 'Rollback blocked: roleless counterparty accounts exist. Keep the expanded schema and use a compatible forward fix.', 1;
            END;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_CompanyPartyAccounts_Roles",
            table: "CompanyPartyAccounts",
            schema: "counterparty");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CompanyPartyAccounts_Roles",
            table: "CompanyPartyAccounts",
            schema: "counterparty",
            sql: "[Roles] BETWEEN 1 AND 15");
    }
}
