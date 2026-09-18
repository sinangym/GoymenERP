using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations;

/// <summary>
/// ACC-4: the canonical content hash of each delivery (ORG #298 P3-1). NOT NULL, with no default:
/// no delivery row predates ACC-4 in any environment, and a table that had one would fail this
/// migration visibly instead of keeping a row that replays without a content check (#307 review).
/// </summary>
[DbContext(typeof(AccountingDbContext))]
[Migration("20260918220000_AddDeliveryRequestHash")]
public sealed class AddDeliveryRequestHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RequestHash",
            schema: "accounting",
            table: "Deliveries",
            type: "varchar(64)",
            unicode: false,
            maxLength: 64,
            nullable: false);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Deliveries_RequestHash",
            schema: "accounting",
            table: "Deliveries",
            sql: "LEN([RequestHash]) = 64");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Deliveries_RequestHash",
            schema: "accounting",
            table: "Deliveries");

        migrationBuilder.DropColumn(
            name: "RequestHash",
            schema: "accounting",
            table: "Deliveries");
    }
}
