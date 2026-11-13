using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadingInstructionCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "logistics",
                table: "LoadingInstructions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "logistics",
                table: "LoadingInstructions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                schema: "logistics",
                table: "LoadingInstructions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructions_Status",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructions_Status",
                schema: "logistics",
                table: "LoadingInstructions",
                sql: "[Status] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructions_Cancel",
                schema: "logistics",
                table: "LoadingInstructions",
                sql: "([Status] = 3 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND LEN([CancelReason]) > 0) OR ([Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancelReason] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a Cancelled instruction (status 3) would lose its meaning (it would read back as Draft/Issued), so Down refuses.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [logistics].[LoadingInstructions] WHERE [Status] = 3) "
                + "THROW 51265, 'Cancelled loading instructions exist; AddLoadingInstructionCancel is not rolled back (forward fix).', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructions_Cancel",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingInstructions_Status",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                schema: "logistics",
                table: "LoadingInstructions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingInstructions_Status",
                schema: "logistics",
                table: "LoadingInstructions",
                sql: "[Status] IN (1, 2)");
        }
    }
}
