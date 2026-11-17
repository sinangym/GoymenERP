using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditMovementProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditMovementProposals",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsReversal = table.Column<bool>(type: "bit", nullable: false),
                    SourceHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MappingStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MappingVersion = table.Column<long>(type: "bigint", nullable: true),
                    WaitingReasonsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditMovementProposals", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CreditMovementProposals_Mapping", "([MappingStatus] = N'MappingMissing' AND [MappingVersion] IS NULL) OR ([MappingStatus] = N'MappingDetailsPending' AND [MappingVersion] > 0)");
                    table.CheckConstraint("CK_CreditMovementProposals_Source", "ISJSON([SourceJson]) = 1 AND LEN([SourceHash]) = 64 AND ISJSON([WaitingReasonsJson]) = 1");
                    table.ForeignKey(
                        name: "FK_CreditMovementProposals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditMovementProposals_CompanyId_MovementId_IsReversal",
                schema: "accounting",
                table: "CreditMovementProposals",
                columns: new[] { "CompanyId", "MovementId", "IsReversal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[CreditMovementProposals])
                    THROW 51971, 'An immutable credit movement proposal source exists; use a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "CreditMovementProposals",
                schema: "accounting");
        }
    }
}
