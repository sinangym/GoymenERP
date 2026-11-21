using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditOutcomeQueryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F023-5a: serves the outcome-filtered company read on the same keyset as the plain read.
            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CompanyId_Outcome_OccurredAtUtc_Id",
                schema: "audit",
                table: "AuditEvents",
                columns: new[] { "CompanyId", "Outcome", "OccurredAtUtc", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // F023-5a: dropping a read index loses no row, so Down needs no forward-fix guard.
            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_CompanyId_Outcome_OccurredAtUtc_Id",
                schema: "audit",
                table: "AuditEvents");
        }
    }
}
