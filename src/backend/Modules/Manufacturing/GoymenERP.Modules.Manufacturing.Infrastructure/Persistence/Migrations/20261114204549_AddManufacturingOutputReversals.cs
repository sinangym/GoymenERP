using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingOutputReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OutputLineId",
                schema: "manufacturing",
                table: "Facts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputLineKind",
                schema: "manufacturing",
                table: "Facts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputLineSequence",
                schema: "manufacturing",
                table: "Facts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutputReversals",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputKind = table.Column<int>(type: "int", nullable: false),
                    OutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentRollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MassKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputReversals", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_OutputReversals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [OutputId] <> '00000000-0000-0000-0000-000000000000' AND [ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OutputReversals_Mass", "[OutputKind] = 3 AND [MassKg] > 0");
                    table.CheckConstraint("CK_OutputReversals_Text", "LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_OutputReversals_Facts_CompanyId_OutputKind_OutputId",
                        columns: x => new { x.CompanyId, x.OutputKind, x.OutputId },
                        principalSchema: "manufacturing",
                        principalTable: "Facts",
                        principalColumns: new[] { "CompanyId", "Kind", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutputReversals_Orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "manufacturing",
                        principalTable: "Orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Facts_OutputLine",
                schema: "manufacturing",
                table: "Facts",
                sql: "([OutputLineKind] IS NULL AND [OutputLineSequence] IS NULL AND [OutputLineId] IS NULL) OR ([Kind] = 3 AND (([OutputLineKind] = 1 AND [OutputClass] = 1 AND [OutputLineSequence] >= 1 AND [OutputLineId] IS NULL) OR ([OutputLineKind] = 2 AND [OutputClass] = 1 AND [OutputLineSequence] IS NULL AND [OutputLineId] IS NOT NULL AND [OutputLineId] <> '00000000-0000-0000-0000-000000000000') OR ([OutputLineKind] = 3 AND [OutputLineSequence] IS NULL AND [OutputLineId] IS NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_OutputReversals_CompanyId_OrderId",
                schema: "manufacturing",
                table: "OutputReversals",
                columns: new[] { "CompanyId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputReversals_CompanyId_OutputKind_OutputId",
                schema: "manufacturing",
                table: "OutputReversals",
                columns: new[] { "CompanyId", "OutputKind", "OutputId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [manufacturing].[OutputReversals]) OR EXISTS (SELECT 1 FROM [manufacturing].[Facts] WHERE [OutputLineKind] IS NOT NULL) BEGIN THROW 51284, 'Output reversals or plan output line references exist; AddManufacturingOutputReversals is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "OutputReversals",
                schema: "manufacturing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Facts_OutputLine",
                schema: "manufacturing",
                table: "Facts");

            migrationBuilder.DropColumn(
                name: "OutputLineId",
                schema: "manufacturing",
                table: "Facts");

            migrationBuilder.DropColumn(
                name: "OutputLineKind",
                schema: "manufacturing",
                table: "Facts");

            migrationBuilder.DropColumn(
                name: "OutputLineSequence",
                schema: "manufacturing",
                table: "Facts");
        }
    }
}
