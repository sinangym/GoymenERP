using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdoptManufacturingProfileSingleFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgToleranceVersions_Lifecycle",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgTolerance_Approval",
                schema: "settings",
                table: "ManufacturingTolerancePolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgParameterProfiles_Lifecycle",
                schema: "settings",
                table: "ManufacturingParameterProfiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgToleranceVersions_Lifecycle",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions",
                sql: "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND (([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Published' AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0 AND (([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL) OR (LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgTolerance_Approval",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                sql: "[ApprovalStatus] IN ('Approved','Published') AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedAtUtc] > '0001-01-01T00:00:00+00:00' AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgParameterProfiles_Lifecycle",
                schema: "settings",
                table: "ManufacturingParameterProfiles",
                sql: "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND (([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Published' AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0 AND (([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL) OR (LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix (URN-D6): a version published directly or by its preparer, or a registry row authorized by its
            // publication, is recorded history the approval-era checks would reject; it is never dropped or rewritten.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[ManufacturingParameterProfiles]
                           WHERE [Status] = 'Published' AND ([ApprovedBy] IS NULL OR [PublishedBy] = [PreparedBy]))
                    OR EXISTS (SELECT 1 FROM [settings].[ManufacturingToleranceProfileVersions]
                               WHERE [Status] = 'Published' AND ([ApprovedBy] IS NULL OR [PublishedBy] = [PreparedBy]))
                    OR EXISTS (SELECT 1 FROM [settings].[ManufacturingTolerancePolicies] WHERE [ApprovalStatus] = 'Published')
                BEGIN
                    THROW 51345, 'Single-flow manufacturing profile publications exist; AdoptManufacturingProfileSingleFlow is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgToleranceVersions_Lifecycle",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgTolerance_Approval",
                schema: "settings",
                table: "ManufacturingTolerancePolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgParameterProfiles_Lifecycle",
                schema: "settings",
                table: "ManufacturingParameterProfiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgToleranceVersions_Lifecycle",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions",
                sql: "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND (([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Published' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedBy] <> [PreparedBy] AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgTolerance_Approval",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                sql: "[ApprovalStatus] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedAtUtc] > '0001-01-01T00:00:00+00:00' AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgParameterProfiles_Lifecycle",
                schema: "settings",
                table: "ManufacturingParameterProfiles",
                sql: "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([PreparationReason]))) > 0 AND (([Status] = 'Draft' AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL) OR ([Status] = 'Published' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedBy] <> [PreparedBy] AND [ApprovedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0 AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND [PublishedBy] <> [PreparedBy] AND [PublishedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([PublicationReason]))) > 0))");
        }
    }
}
