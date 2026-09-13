using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Idempotency.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGroupSharedTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "idempotency");

            migrationBuilder.CreateTable(
                name: "GroupSharedCommandTerminals",
                schema: "idempotency",
                columns: table => new
                {
                    Subject = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CommandName = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LocalActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupSharedCommandTerminals", x => new { x.Subject, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_GroupSharedTerminals_ActorOrigin", "[LocalActorId] <> '00000000-0000-0000-0000-000000000000' AND [OriginCompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_GroupSharedTerminals_CompletedUtc", "DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
                    table.CheckConstraint("CK_GroupSharedTerminals_Hashes", "DATALENGTH([IdempotencyKeyHash]) = 64 AND [IdempotencyKeyHash] NOT LIKE '%[^0-9A-F]%' AND DATALENGTH([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9A-F]%'");
                    table.CheckConstraint("CK_GroupSharedTerminals_ProductCommands", "[Subject] = 'Product' AND [CommandName] IN ('product.draft.create.v1', 'product.draft.basic-edit.v1')");
                    table.CheckConstraint("CK_GroupSharedTerminals_Response", "ISJSON([ResponseJson], VALUE) = 1 AND [StatusCode] BETWEEN 200 AND 599 AND [SchemaVersion] >= 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [idempotency].[GroupSharedCommandTerminals])
                    THROW 51043, 'Committed Product replay records must be retained; use a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "GroupSharedCommandTerminals",
                schema: "idempotency");
        }
    }
}
