using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseUnorderedAcceptanceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnorderedAcceptanceFacts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnorderedAcceptanceFacts", x => new { x.CompanyId, x.AcceptanceId, x.AcceptanceLineId });
                    table.CheckConstraint("CK_UnorderedAcceptanceFacts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceLineId] <> '00000000-0000-0000-0000-000000000000' AND [Version] = 1");
                    table.CheckConstraint("CK_UnorderedAcceptanceFacts_Json", "ISJSON([RecordJson]) = 1 AND LEN([RecordHash]) = 64");
                    table.ForeignKey(
                        name: "FK_UnorderedAcceptanceFacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnorderedAcceptanceAttempts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnorderedAcceptanceAttempts", x => new { x.CompanyId, x.KeyHash });
                    table.CheckConstraint("CK_UnorderedAcceptanceAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceLineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_UnorderedAcceptanceAttempts_Receipt", "LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([RecordJson]) = 1");
                    table.ForeignKey(
                        name: "FK_UnorderedAcceptanceAttempts_UnorderedAcceptanceFacts_CompanyId_AcceptanceId_AcceptanceLineId",
                        columns: x => new { x.CompanyId, x.AcceptanceId, x.AcceptanceLineId },
                        principalSchema: "purchasing",
                        principalTable: "UnorderedAcceptanceFacts",
                        principalColumns: new[] { "CompanyId", "AcceptanceId", "AcceptanceLineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnorderedAcceptanceAttempts_CompanyId_AcceptanceId_AcceptanceLineId",
                schema: "purchasing",
                table: "UnorderedAcceptanceAttempts",
                columns: new[] { "CompanyId", "AcceptanceId", "AcceptanceLineId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[UnorderedAcceptanceFacts])
                    OR EXISTS (SELECT 1 FROM [purchasing].[UnorderedAcceptanceAttempts])
                BEGIN
                    THROW 52320, 'Retained unordered acceptance facts or original receipts exist; AddPurchaseUnorderedAcceptanceFacts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "UnorderedAcceptanceAttempts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "UnorderedAcceptanceFacts",
                schema: "purchasing");
        }
    }
}
