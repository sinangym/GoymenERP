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
            // KIM-D35: ten repeated ones is not a VKN. The fiscal runtime seed wrote it as a stand-in on the placeholder
            // company only; the company's VKN is not known, so it is cleared and the company is "VKN girilmemiş" until a
            // real VKN passes the maker-checker change flow (KIM-D28). Only that exact stand-in on a placeholder company
            // that issues no invoice is touched: a real or already approved VKN is never rewritten. The schema allows a
            // placeholder without a VKN (CK_Companies_Vkn_RequiredUnlessPlaceholder), so no constraint changes. The row
            // version moves so a screen that holds the old version is refused, not silently overwritten. The migration
            // history records this change; no audit event is written from the migration.
            migrationBuilder.Sql("""
                UPDATE [organization].[Companies]
                SET [Vkn] = NULL,
                    [Version] = [Version] + 1,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [IsPlaceholder] = 1
                  AND [InvoiceCapable] = 0
                  AND [Vkn] = N'1111111111';
                """);
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
