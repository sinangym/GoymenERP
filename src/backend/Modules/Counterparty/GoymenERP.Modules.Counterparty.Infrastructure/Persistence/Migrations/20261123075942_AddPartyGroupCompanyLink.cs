using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyGroupCompanyLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupCompanyId",
                schema: "counterparty",
                table: "Parties",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_GroupCompanyId",
                schema: "counterparty",
                table: "Parties",
                column: "GroupCompanyId",
                filter: "[GroupCompanyId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Parties_Companies_GroupCompanyId",
                schema: "counterparty",
                table: "Parties",
                column: "GroupCompanyId",
                principalSchema: "organization",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP14 forward fix: a recorded party ↔ group company link (YOL-D31) is the intercompany report's only definition of a
            // group company; while one exists the column is not dropped. Without links the column rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[Parties] WHERE [GroupCompanyId] IS NOT NULL)
                BEGIN
                    THROW 52271, 'Party group-company links exist; AddPartyGroupCompanyLink is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Parties_Companies_GroupCompanyId",
                schema: "counterparty",
                table: "Parties");

            migrationBuilder.DropIndex(
                name: "IX_Parties_GroupCompanyId",
                schema: "counterparty",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "GroupCompanyId",
                schema: "counterparty",
                table: "Parties");
        }
    }
}
