using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// Q9 cutover: existing orders keep their previous behavior; only orders created by the new application
/// set CustomerAcceptanceRequired. Acceptance is tied to a commercial revision and does not create stock,
/// allocation or money effects. Down refuses after the cutover is used rather than silently forgetting it.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260930223000_AddDraftCustomerAcceptance")]
public sealed class AddDraftCustomerAcceptance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CustomerAcceptanceRequired", schema: "sales", table: "Drafts", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AlterColumn<bool>(
            name: "CustomerAcceptanceRequired", schema: "sales", table: "Drafts", type: "bit",
            nullable: false, oldClrType: typeof(bool), oldType: "bit", oldDefaultValue: false);
        migrationBuilder.AddColumn<int>(
            name: "CustomerAcceptedRevision", schema: "sales", table: "Drafts", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "CustomerAcceptedBy", schema: "sales", table: "Drafts", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CustomerAcceptedAtUtc", schema: "sales", table: "Drafts", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_CustomerAcceptance", schema: "sales", table: "Drafts",
            sql: SalesDbContext.CustomerAcceptanceConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[Drafts]
                       WHERE [CustomerAcceptanceRequired] = 1 OR [CustomerAcceptedRevision] IS NOT NULL)
                THROW 51059, 'Sales customer acceptance cutover requires a forward fix.', 1;
            """);
        migrationBuilder.DropCheckConstraint(name: "CK_Drafts_CustomerAcceptance", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "CustomerAcceptedAtUtc", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "CustomerAcceptedBy", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "CustomerAcceptedRevision", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "CustomerAcceptanceRequired", schema: "sales", table: "Drafts");
    }
}
