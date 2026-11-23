using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccountCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "identity",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // KIM-D57 forward fix: a recorded creator is the only fact that lets that administrator see an unassigned account.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[Users] WHERE [CreatedByUserId] IS NOT NULL)
                BEGIN
                    THROW 52082, 'Account creator records exist; AddUserAccountCreatedBy is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "identity",
                table: "Users");
        }
    }
}
