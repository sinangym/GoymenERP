using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOutboxDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxDeliveries",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ConsumerName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    FirstAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxDeliveries", x => new { x.CompanyId, x.SourceName, x.ConsumerName, x.MessageId });
                    table.CheckConstraint("CK_WorkOutboxDeliveries_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_WorkOutboxDeliveries_State", "[Status] IN (1,2,3) AND [AttemptCount] >= 1 AND [SchemaVersion] >= 1 AND LEN([EventType]) > 0 AND [MessageId] <> '00000000-0000-0000-0000-000000000000' AND [LastAttemptAtUtc] >= [FirstAttemptAtUtc] AND DATALENGTH([PayloadSha256]) = 64 AND [PayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND (([Status] = 1 AND [DeliveredAtUtc] IS NOT NULL AND [NextAttemptAtUtc] IS NULL) OR ([Status] = 2 AND [DeliveredAtUtc] IS NULL AND [NextAttemptAtUtc] IS NOT NULL AND [LastErrorCode] IS NOT NULL) OR ([Status] = 3 AND [DeliveredAtUtc] IS NULL AND [NextAttemptAtUtc] IS NULL AND [LastErrorCode] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_OutboxDeliveries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxSourceCursors",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Position = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxSourceCursors", x => new { x.CompanyId, x.SourceName });
                    table.CheckConstraint("CK_WorkOutboxCursors_State", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([SourceName]) > 0 AND LEN([Position]) > 0");
                    table.ForeignKey(
                        name: "FK_OutboxSourceCursors_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOutboxDeliveries_Open",
                schema: "work",
                table: "OutboxDeliveries",
                columns: new[] { "CompanyId", "Status", "LastAttemptAtUtc" },
                filter: "[Status] <> 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[OutboxDeliveries]) OR EXISTS (SELECT 1 FROM [work].[OutboxSourceCursors])
                BEGIN
                    THROW 51234, 'Work outbox dispatch records (cursors, inbox/dedup, retry, poison) exist; AddWorkOutboxDispatch is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OutboxDeliveries",
                schema: "work");

            migrationBuilder.DropTable(
                name: "OutboxSourceCursors",
                schema: "work");
        }
    }
}
