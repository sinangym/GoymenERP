using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationOperatorParty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperatorPartyId",
                schema: "organization",
                table: "Locations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Locations_InternalOperatorParty",
                schema: "organization",
                table: "Locations",
                sql: "[IsExternal] = 1 OR [OperatorPartyId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Locations_OperatorParty_NotEmpty",
                schema: "organization",
                table: "Locations",
                sql: "[OperatorPartyId] IS NULL OR [OperatorPartyId] <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Locations_InternalOperatorParty",
                schema: "organization",
                table: "Locations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Locations_OperatorParty_NotEmpty",
                schema: "organization",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "OperatorPartyId",
                schema: "organization",
                table: "Locations");
        }
    }
}
