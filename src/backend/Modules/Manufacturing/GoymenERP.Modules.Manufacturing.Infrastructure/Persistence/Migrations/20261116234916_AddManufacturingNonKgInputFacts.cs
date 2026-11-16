using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingNonKgInputFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NonKgInputFacts",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Item = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReplacesFactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonKgInputFacts", x => new { x.CompanyId, x.OrderId, x.Revision });
                    table.UniqueConstraint("AK_NonKgInputFacts_CompanyId_OrderId_FactId", x => new { x.CompanyId, x.OrderId, x.FactId });
                    table.CheckConstraint("CK_NonKgInputFacts_Shape", "[Revision] > 0 AND [Kind] IN (1,2) AND [Unit] IN (1,2,3) AND ([Kind] <> 1 OR [Unit] = 1) AND LEN([Item]) > 0 AND LEN([Quantity]) > 0 AND LEN([Reason]) > 0 AND ([ReplacesFactId] IS NULL OR [ReplacesFactId] <> [FactId]) AND ([Quantity] <> '0' OR [ReplacesFactId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_NonKgInputFacts_NonKgInputFacts_CompanyId_OrderId_ReplacesFactId",
                        columns: x => new { x.CompanyId, x.OrderId, x.ReplacesFactId },
                        principalSchema: "manufacturing",
                        principalTable: "NonKgInputFacts",
                        principalColumns: new[] { "CompanyId", "OrderId", "FactId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonKgInputFacts_Orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "manufacturing",
                        principalTable: "Orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NonKgInputFacts_CompanyId_FactId",
                schema: "manufacturing",
                table: "NonKgInputFacts",
                columns: new[] { "CompanyId", "FactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonKgInputFacts_CompanyId_OrderId_ReplacesFactId",
                schema: "manufacturing",
                table: "NonKgInputFacts",
                columns: new[] { "CompanyId", "OrderId", "ReplacesFactId" },
                unique: true,
                filter: "[ReplacesFactId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[NonKgInputFacts])
                BEGIN
                    THROW 51963, 'Actual non-kg consumption history exists; AddManufacturingNonKgInputFacts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "NonKgInputFacts",
                schema: "manufacturing");
        }
    }
}
