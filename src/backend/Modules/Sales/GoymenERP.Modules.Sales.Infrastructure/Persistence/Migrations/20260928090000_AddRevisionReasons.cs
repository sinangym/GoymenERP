using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// DEC-SAL-002 (KILITLI: "… değişirse yeni revizyon ve yeniden onay gerekir"; "Rev.0 korunur") and AT-023: the quantity
/// revision archives the approved revision it supersedes WITH the reason the user gave. ONE nullable column on the existing
/// append-only <c>sales.DraftRevisions</c>: the reason is null for a row a price revision archived (that command carries no
/// reason) and non-blank otherwise. No row is rewritten and no other table changes; the write guard admits no column of the
/// archive. (The line's quantity columns become writable under the quantity revision in the write guard — a model rule, not
/// a schema change.)
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260928090000_AddRevisionReasons")]
public sealed class AddRevisionReasons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Reason",
            schema: "sales",
            table: "DraftRevisions",
            type: "nvarchar(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftRevisions_Reason",
            schema: "sales",
            table: "DraftRevisions",
            sql: "[Reason] IS NULL OR LEN([Reason]) > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // "Rev.0 korunur": a recorded revision reason is part of the approved history; dropping the column would lose why an
        // approved revision was superseded. While any reason exists the rollback is refused with this module's own error
        // (51057 — 51056 is Logistics' on main, so the next free number; used by no other module); a database whose archive
        // holds no reason rolls back freely. The THROW precedes every destructive operation. Forward fix, the
        // AddDeliverySchedules (51054) / AddDraftCancellations (51055) pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftRevisions] WHERE [Reason] IS NOT NULL)
                THROW 51057, 'Sales revision reasons exist; AddRevisionReasons is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_DraftRevisions_Reason",
            schema: "sales",
            table: "DraftRevisions");

        migrationBuilder.DropColumn(
            name: "Reason",
            schema: "sales",
            table: "DraftRevisions");
    }
}
