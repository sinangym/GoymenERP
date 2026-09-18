using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// MFG-API (#313, ADR-011): the module-local command log. One row per (company, command name,
/// idempotency key hash); the key itself is never stored, only its uppercase SHA-256. Append-only.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260918230000_AddManufacturingCommands")]
public sealed class AddManufacturingCommands : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Commands",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OriginalCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Commands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_Commands_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                table.CheckConstraint("CK_Commands_ResultJson", "ISJSON([ResultJson]) = 1");
                table.CheckConstraint("CK_Commands_Text",
                    "LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([OriginalCorrelationId]) > 0");
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
        migrationBuilder.DropTable(name: "Commands", schema: "manufacturing");
    }
}
