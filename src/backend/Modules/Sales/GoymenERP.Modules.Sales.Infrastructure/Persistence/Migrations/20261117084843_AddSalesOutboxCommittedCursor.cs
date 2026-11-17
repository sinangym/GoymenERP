using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOutboxCommittedCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Position",
                schema: "sales",
                table: "OutboxMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "sales",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[OutboxMessages])
                BEGIN
                    THROW 51983, 'Sales outbox events retain cursor evidence; AddSalesOutboxCommittedCursor requires a forward fix.', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "sales",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "sales",
                table: "OutboxMessages");
        }
    }
}
