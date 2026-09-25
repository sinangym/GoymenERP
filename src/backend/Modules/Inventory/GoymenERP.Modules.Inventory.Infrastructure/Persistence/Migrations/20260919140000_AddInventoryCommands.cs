using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// INV-OPEN-1 part 2: Inventory's ADR-011 command record — one row per (company, command, key hash) with
/// the actor, the canonical payload hash and the result a replay repeats. No row is written here. Down
/// refuses (51046) while any command exists: a recorded answer is never dropped by a rollback (forward fix,
/// as #329/#334).
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260919140000_AddInventoryCommands")]
public sealed class AddInventoryCommands : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Commands",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Commands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_Commands_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([PayloadHash]) = 64");
                table.CheckConstraint("CK_Commands_Text",
                    "LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
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
            IF EXISTS (SELECT 1 FROM [inventory].[Commands])
                THROW 51046, 'Inventory command records require a forward fix.', 1;
            """);

        migrationBuilder.DropTable(
            name: "Commands",
            schema: "inventory");
    }
}
