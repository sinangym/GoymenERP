using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations;

/// <summary>
/// ACC-V0: a period opening is maker≠checker (Ö4, #66 5722637822 "dönem açma … hazırlayandan farklı
/// tek yetkili onayında"), and the Accounting HTTP commands keep their ADR-011 attempt records.
/// <list type="bullet">
/// <item><c>Periods</c> gains the preparer (NOT NULL, with NO default) and the approval. No period row
/// exists in any environment: Accounting was never host-wired, so nothing could propose or open one.
/// A table that had one fails this migration visibly (the ACC-4 <c>AddDeliveryRequestHash</c>
/// precedent) rather than keeping an open period that no second person approved; <c>CK_Periods_Prepared</c>
/// also refuses an empty preparer. <c>CK_Periods_Status</c> binds the facts to the status, including
/// approver ≠ preparer.</item>
/// <item><c>Commands</c>: one row per (company, command, idempotency-key hash).</item>
/// <item><c>Down</c> refuses (<c>THROW 51048</c>, Accounting's module number) while any period or any
/// command record exists: dropping them would forget who prepared and who approved a period, and the
/// replay identity of every command. That is a forward fix, never a silent loss.</item>
/// </list>
/// </summary>
[DbContext(typeof(AccountingDbContext))]
[Migration("20260919140000_AddPeriodApproval")]
public sealed class AddPeriodApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Periods_Status",
            schema: "accounting",
            table: "Periods");

        migrationBuilder.AddColumn<Guid>(
            name: "PreparedByUserId",
            schema: "accounting",
            table: "Periods",
            type: "uniqueidentifier",
            nullable: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PreparedAtUtc",
            schema: "accounting",
            table: "Periods",
            type: "datetimeoffset",
            nullable: false);

        migrationBuilder.AddColumn<Guid>(
            name: "ApprovedByUserId",
            schema: "accounting",
            table: "Periods",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ApprovedAtUtc",
            schema: "accounting",
            table: "Periods",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Periods_Prepared",
            schema: "accounting",
            table: "Periods",
            sql: "[PreparedByUserId] <> '00000000-0000-0000-0000-000000000000'");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Periods_Status",
            schema: "accounting",
            table: "Periods",
            sql: AccountingDbContext.PeriodStatusCheck);

        migrationBuilder.CreateTable(
            name: "Commands",
            schema: "accounting",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Commands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_Commands_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_Commands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
                table.CheckConstraint("CK_Commands_PayloadHash", "LEN([PayloadHash]) = 64");
                table.CheckConstraint("CK_Commands_ResultJson", "ISJSON([ResultJson]) = 1");
                table.ForeignKey(
                    name: "FK_Commands_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [accounting].[Periods]) OR EXISTS (SELECT 1 FROM [accounting].[Commands])
                THROW 51048, 'Accounting period approvals and command records require a forward fix.', 1;
            """);

        migrationBuilder.DropTable(
            name: "Commands",
            schema: "accounting");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Periods_Status",
            schema: "accounting",
            table: "Periods");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Periods_Prepared",
            schema: "accounting",
            table: "Periods");

        migrationBuilder.DropColumn(name: "ApprovedAtUtc", schema: "accounting", table: "Periods");
        migrationBuilder.DropColumn(name: "ApprovedByUserId", schema: "accounting", table: "Periods");
        migrationBuilder.DropColumn(name: "PreparedAtUtc", schema: "accounting", table: "Periods");
        migrationBuilder.DropColumn(name: "PreparedByUserId", schema: "accounting", table: "Periods");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Periods_Status",
            schema: "accounting",
            table: "Periods",
            sql: "([Status] = 1 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) OR ([Status] = 2 AND [ClosedByUserId] IS NOT NULL AND [ClosedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ClosedAtUtc] IS NOT NULL)");
    }
}
