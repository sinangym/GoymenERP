using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkInboxConsumerDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_WorkOutboxDeliveries_ConsumerMessage",
                schema: "work",
                table: "OutboxDeliveries",
                columns: new[] { "CompanyId", "ConsumerName", "MessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the index loses no row (the primary key still keeps one row per source), so Down needs no guard.
            migrationBuilder.DropIndex(
                name: "UX_WorkOutboxDeliveries_ConsumerMessage",
                schema: "work",
                table: "OutboxDeliveries");
        }
    }
}
