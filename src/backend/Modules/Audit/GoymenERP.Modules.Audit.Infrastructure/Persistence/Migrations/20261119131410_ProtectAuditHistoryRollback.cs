using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAuditHistoryRollback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migration history marks the append-only rollback boundary; the model is unchanged.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [audit].[AuditEvents] WITH (TABLOCKX, HOLDLOCK))
                BEGIN
                    THROW 51900, 'Audit history exists; ProtectAuditHistoryRollback requires a forward fix.', 1;
                END;
                """);
        }
    }
}
