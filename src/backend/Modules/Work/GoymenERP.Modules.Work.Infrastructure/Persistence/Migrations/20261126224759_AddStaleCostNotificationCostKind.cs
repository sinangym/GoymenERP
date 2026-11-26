using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaleCostNotificationCostKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostKind",
                schema: "work",
                table: "InboxNotifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_CostKind",
                schema: "work",
                table: "InboxNotifications",
                sql: "([Kind] = 10 AND [CostKind] IS NOT NULL AND [CostKind] IN (1, 2)) OR ([Kind] <> 10 AND [CostKind] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[InboxNotifications] WHERE [CostKind] IS NOT NULL)
                    THROW 52143, 'Stale cost notification source kind evidence exists; forward fix required.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_CostKind",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "CostKind",
                schema: "work",
                table: "InboxNotifications");
        }
    }
}
