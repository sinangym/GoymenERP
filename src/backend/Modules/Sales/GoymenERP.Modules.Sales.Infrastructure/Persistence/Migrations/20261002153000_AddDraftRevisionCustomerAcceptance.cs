using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// Captures an archived revision's exact customer-acceptance fact. All columns are nullable so an
/// older archive never becomes a fabricated legacy exemption; consumers fail closed when the
/// immutable draft root requires acceptance and these facts are absent.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20261002153000_AddDraftRevisionCustomerAcceptance")]
public sealed class AddDraftRevisionCustomerAcceptance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CustomerAcceptedRevision", schema: "sales", table: "DraftRevisions", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "CustomerAcceptedBy", schema: "sales", table: "DraftRevisions", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CustomerAcceptedAtUtc", schema: "sales", table: "DraftRevisions", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftRevisions_CustomerAcceptance", schema: "sales", table: "DraftRevisions",
            sql: SalesDbContext.RevisionCustomerAcceptanceConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftRevisions]
                       WHERE [CustomerAcceptedRevision] IS NOT NULL OR [CustomerAcceptedBy] IS NOT NULL OR [CustomerAcceptedAtUtc] IS NOT NULL)
                THROW 51065, 'Sales archived revision customer acceptance requires a forward fix.', 1;
            """);
        migrationBuilder.DropCheckConstraint(name: "CK_DraftRevisions_CustomerAcceptance", schema: "sales", table: "DraftRevisions");
        migrationBuilder.DropColumn(name: "CustomerAcceptedAtUtc", schema: "sales", table: "DraftRevisions");
        migrationBuilder.DropColumn(name: "CustomerAcceptedBy", schema: "sales", table: "DraftRevisions");
        migrationBuilder.DropColumn(name: "CustomerAcceptedRevision", schema: "sales", table: "DraftRevisions");
    }
}
