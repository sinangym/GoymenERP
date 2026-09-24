using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// LOG-5 Inventory persistence: versioned physical allocations and unpublished producer-owned
    /// outbox envelopes. It creates no allocation, grant, dispatcher or runtime registration.
    /// </summary>
    public sealed partial class AddInventoryDefiniteShipmentRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Allocations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerKind = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustodianKind = table.Column<int>(type: "int", nullable: false),
                    CustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Block = table.Column<int>(type: "int", nullable: false),
                    AllocatedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RemainingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.UniqueConstraint("AK_Allocations_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Allocations_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OwnerId] <> '00000000-0000-0000-0000-000000000000' AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Allocations_Quantity", "[AllocatedKg] > 0 AND [RemainingKg] >= 0 AND [RemainingKg] <= [AllocatedKg] AND (([Status] = 1 AND [RemainingKg] > 0) OR ([Status] = 2 AND [RemainingKg] = 0))");
                    table.CheckConstraint("CK_Allocations_State", "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] = 1 AND [Status] IN (1, 2) AND [Version] >= 1");
                    table.ForeignKey(
                        name: "FK_Allocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Allocations_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "inventory",
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
                name: "IX_Allocations_CompanyId_LabelId",
                schema: "inventory",
                table: "Allocations",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_CompanyId_LocationId",
                schema: "inventory",
                table: "Allocations",
                columns: new[] { "CompanyId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "UX_OutboxMessages_AggregateSequence",
                schema: "inventory",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "EventType", "AggregateId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[Allocations])
                   OR EXISTS (SELECT 1 FROM [inventory].[OutboxMessages])
                    THROW 51047, 'Inventory definite-shipment records require a forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "Allocations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "inventory");
        }
    }
}
