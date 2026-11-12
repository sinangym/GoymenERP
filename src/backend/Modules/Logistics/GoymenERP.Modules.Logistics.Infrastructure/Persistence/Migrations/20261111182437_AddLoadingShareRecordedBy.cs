using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadingShareRecordedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordedBy",
                schema: "logistics",
                table: "LoadingShares",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingShares_RecordedBy",
                schema: "logistics",
                table: "LoadingShares",
                sql: "[RecordedBy] IS NULL OR LEN([RecordedBy]) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: the recorded shipper is the evidence SAT-D216 checks; dropping it would silently reopen self-approval.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [logistics].[LoadingShares] WHERE [RecordedBy] IS NOT NULL) "
                + "THROW 51260, 'Loading share shipper identities exist; AddLoadingShareRecordedBy is not rolled back (forward fix).', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingShares_RecordedBy",
                schema: "logistics",
                table: "LoadingShares");

            migrationBuilder.DropColumn(
                name: "RecordedBy",
                schema: "logistics",
                table: "LoadingShares");
        }
    }
}
