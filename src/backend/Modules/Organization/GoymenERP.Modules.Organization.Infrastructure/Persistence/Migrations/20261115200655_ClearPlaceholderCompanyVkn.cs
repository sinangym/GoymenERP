using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClearPlaceholderCompanyVkn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KIM-D35, superseded by KIM-D41 + KIM-D48: this migration no longer rewrites the company directly. A VKN data
            // fix must leave a per-row approval trail with a system actor, so the stand-in "VKN" is staged by
            // StageCompanyTaxIdCorrections and cleared only when an approver applies it. Intentionally empty; the
            // migration stays in the history so its identifier and order do not change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // KIM-D35: intentionally empty. Rolling back loses nothing: the cleared value was not a VKN and is not
            // written back, and a VKN approved since then stays (forward fix). The older schema holds a placeholder
            // without a VKN as it is.
        }
    }
}
