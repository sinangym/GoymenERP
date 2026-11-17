using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashTransferExchangeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangeSnapshotJson",
                schema: "finance",
                table: "CashTransfers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransfers_ExchangeSnapshot",
                schema: "finance",
                table: "CashTransfers",
                sql: "[ExchangeSnapshotJson] IS NULL OR ([Kind] = 1 AND ISJSON([ExchangeSnapshotJson]) = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CashTransfers] WHERE [ExchangeSnapshotJson] IS NOT NULL)
                BEGIN
                    THROW 51933, 'Recorded exchange source evidence exists; AddCashTransferExchangeSnapshots is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransfers_ExchangeSnapshot",
                schema: "finance",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "ExchangeSnapshotJson",
                schema: "finance",
                table: "CashTransfers");
        }
    }
}
