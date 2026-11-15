using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingOutputLabelCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutputLabelCodes",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputKind = table.Column<int>(type: "int", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    GeneratedNumber = table.Column<long>(type: "bigint", nullable: true),
                    Format = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputLabelCodes", x => new { x.CompanyId, x.OutputId });
                    table.CheckConstraint("CK_OutputLabelCodes_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [OutputId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000' AND [OutputKind] = 3");
                    table.CheckConstraint("CK_OutputLabelCodes_Source", "([Source] = 1 AND [GeneratedNumber] IS NULL AND [Format] IS NULL) OR ([Source] = 2 AND [GeneratedNumber] IS NOT NULL AND [GeneratedNumber] > 0 AND [Format] IS NOT NULL AND LEN([Format]) > 0)");
                    table.CheckConstraint("CK_OutputLabelCodes_Text", "LEN([LabelCode]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_OutputLabelCodes_Facts_CompanyId_OutputKind_OutputId",
                        columns: x => new { x.CompanyId, x.OutputKind, x.OutputId },
                        principalSchema: "manufacturing",
                        principalTable: "Facts",
                        principalColumns: new[] { "CompanyId", "Kind", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutputLabelCodes_Orders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "manufacturing",
                        principalTable: "Orders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutputLabelCodes_CompanyId_GeneratedNumber",
                schema: "manufacturing",
                table: "OutputLabelCodes",
                columns: new[] { "CompanyId", "GeneratedNumber" },
                unique: true,
                filter: "[GeneratedNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutputLabelCodes_CompanyId_LabelCode",
                schema: "manufacturing",
                table: "OutputLabelCodes",
                columns: new[] { "CompanyId", "LabelCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutputLabelCodes_CompanyId_LabelId",
                schema: "manufacturing",
                table: "OutputLabelCodes",
                columns: new[] { "CompanyId", "LabelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutputLabelCodes_CompanyId_OrderId",
                schema: "manufacturing",
                table: "OutputLabelCodes",
                columns: new[] { "CompanyId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutputLabelCodes_CompanyId_OutputKind_OutputId",
                schema: "manufacturing",
                table: "OutputLabelCodes",
                columns: new[] { "CompanyId", "OutputKind", "OutputId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [manufacturing].[OutputLabelCodes]) BEGIN THROW 51731, 'Output label codes exist; AddManufacturingOutputLabelCodes is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "OutputLabelCodes",
                schema: "manufacturing");
        }
    }
}
