using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// FIN-T04b.
/// <list type="bullet">
/// <item>The command record follows ADR-011: keyed by (company, command, key hash). The raw
/// Idempotency-Key column is dropped and never stored again (§2); the actor stays a column and must
/// match on replay (§3).</item>
/// <item>The draft list's stable order gets its index: (company, created, id).</item>
/// </list>
/// No Finance row exists in any environment (the host does not wire Finance), so the key hash is NOT
/// NULL with no default: a table that had rows would fail this migration visibly, never silently.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260919090000_AlignCommandsWithAdr011")]
public sealed class AlignCommandsWithAdr011 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(name: "PK_Commands", schema: "finance", table: "Commands");
        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Identity", schema: "finance", table: "Commands");
        migrationBuilder.DropColumn(name: "IdempotencyKey", schema: "finance", table: "Commands");

        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKeyHash",
            schema: "finance",
            table: "Commands",
            type: "varchar(64)",
            unicode: false,
            maxLength: 64,
            nullable: false);

        migrationBuilder.AddPrimaryKey(
            name: "PK_Commands",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Identity",
            schema: "finance",
            table: "Commands",
            sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_KeyHash",
            schema: "finance",
            table: "Commands",
            sql: "LEN([IdempotencyKeyHash]) = 64");

        migrationBuilder.CreateIndex(
            name: "IX_ReceiptDrafts_CompanyId_CreatedAtUtc_Id",
            schema: "finance",
            table: "ReceiptDrafts",
            columns: new[] { "CompanyId", "CreatedAtUtc", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ReceiptDrafts_CompanyId_CreatedAtUtc_Id",
            schema: "finance",
            table: "ReceiptDrafts");

        migrationBuilder.DropCheckConstraint(name: "CK_Commands_KeyHash", schema: "finance", table: "Commands");
        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Identity", schema: "finance", table: "Commands");
        migrationBuilder.DropPrimaryKey(name: "PK_Commands", schema: "finance", table: "Commands");
        migrationBuilder.DropColumn(name: "IdempotencyKeyHash", schema: "finance", table: "Commands");

        // The raw key cannot be recovered from its hash: with any command row stored this fails visibly.
        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKey",
            schema: "finance",
            table: "Commands",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            collation: "Latin1_General_100_BIN2");

        migrationBuilder.AddPrimaryKey(
            name: "PK_Commands",
            schema: "finance",
            table: "Commands",
            columns: new[] { "CompanyId", "ActorId", "IdempotencyKey" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Identity",
            schema: "finance",
            table: "Commands",
            sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([IdempotencyKey]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
    }
}
