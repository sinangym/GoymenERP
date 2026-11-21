using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// F-027 / DEC-CAR-003: the counterparty module's own append-only, company-scoped outbox
    /// (counterparty.party.deactivated.v1). Down refuses with 51441 while any event exists (forward fix).
    /// </summary>
    public partial class AddCounterpartyOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SourceOperation = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_Envelope", "LEN([EventType]) > 0 AND [SchemaVersion] >= 1 AND LEN([SourceOperation]) > 0 AND LEN([AggregateType]) > 0 AND [AggregateVersion] >= 1 AND [Sequence] >= 1 AND LEN([CorrelationId]) > 0 AND ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_OutboxMessages_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AggregateId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_OutboxMessages_AggregateSequence",
                schema: "counterparty",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "EventType", "AggregateId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: an outbox event is a recorded fact; dropping the table while one exists would lose it.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [counterparty].[OutboxMessages]) BEGIN THROW 51441, 'Counterparty outbox events exist; AddCounterpartyOutbox is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "counterparty");
        }
    }
}
