using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingToleranceProfileLifecycleV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufacturingToleranceProfileLifecycles",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Process = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    MaximumFirePercent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    StateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreparationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PreparationCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparationCausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PreparationSourceChannel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreparationClientIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PreparationUserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovalCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovalCausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovalSourceChannel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApprovalClientIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ApprovalUserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublicationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublicationCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PublicationCausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PublicationSourceChannel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PublicationClientIpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PublicationUserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingToleranceProfileLifecycles", x => new { x.CompanyId, x.ProfileId });
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Approval", "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovalReason] IS NULL AND [ApprovalCorrelationId] IS NULL AND [ApprovalCausationId] IS NULL AND [ApprovalSourceChannel] IS NULL AND [ApprovalClientIpAddress] IS NULL AND [ApprovalUserAgent] IS NULL) OR ([Status] IN (2,3) AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovalReason] IS NOT NULL AND [ApprovalCorrelationId] IS NOT NULL AND [ApprovalSourceChannel] IS NOT NULL AND LEN(TRIM(TRANSLATE([ApprovedBy], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([ApprovalReason], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([ApprovalCorrelationId], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([ApprovalSourceChannel], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0)");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Percent", "[MaximumFirePercent] >= 0");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_PreparationProvenance", "LEN(TRIM(TRANSLATE([PreparedBy], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([PreparationReason], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([PreparationCorrelationId], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([PreparationSourceChannel], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Process", "[Process] IN ('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Publication", "([Status] IN (1,2) AND [PublishedBy] IS NULL AND [PublishedAtUtc] IS NULL AND [PublicationReason] IS NULL AND [PublicationCorrelationId] IS NULL AND [PublicationCausationId] IS NULL AND [PublicationSourceChannel] IS NULL AND [PublicationClientIpAddress] IS NULL AND [PublicationUserAgent] IS NULL) OR ([Status] = 3 AND [PublishedBy] IS NOT NULL AND [PublishedAtUtc] IS NOT NULL AND [PublicationReason] IS NOT NULL AND [PublicationCorrelationId] IS NOT NULL AND [PublicationSourceChannel] IS NOT NULL AND LEN(TRIM(TRANSLATE([PublishedBy], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([PublicationReason], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([PublicationCorrelationId], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0 AND LEN(TRIM(TRANSLATE([PublicationSourceChannel], NCHAR(9) + NCHAR(10) + NCHAR(11) + NCHAR(12) + NCHAR(13) + NCHAR(160), N'      '))) > 0)");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Separation", "([ApprovedBy] IS NULL OR [ApprovedBy] <> [PreparedBy]) AND ([PublishedBy] IS NULL OR [PublishedBy] <> [PreparedBy])");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Status", "[Status] IN (1,2,3)");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Versions", "[ProfileVersion] > 0 AND [StateVersion] > 0");
                    table.CheckConstraint("CK_SettingsMfgToleranceLifecycle_Window", "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingToleranceProfileLifecycles_CompanyId_Process_ProfileVersion",
                schema: "settings",
                table: "ManufacturingToleranceProfileLifecycles",
                columns: new[] { "CompanyId", "Process", "ProfileVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettingsMfgToleranceLifecycle_PublicationWindow",
                schema: "settings",
                table: "ManufacturingToleranceProfileLifecycles",
                columns: new[] { "CompanyId", "Process", "Status", "EffectiveFrom", "EffectiveThrough" });

            migrationBuilder.Sql(
                """
                CREATE OR ALTER TRIGGER [settings].[TR_ManufacturingToleranceProfileLifecycles_Protect]
                ON [settings].[ManufacturingToleranceProfileLifecycles]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF NOT EXISTS (SELECT 1 FROM inserted)
                    BEGIN
                        ;THROW 51010, 'settings_manufacturing_tolerance_lifecycle_delete_forbidden', 1;
                    END;

                    IF NOT EXISTS (SELECT 1 FROM deleted)
                    BEGIN
                        IF EXISTS (SELECT 1 FROM inserted WHERE [Status] <> 1 OR [StateVersion] <> 1)
                        BEGIN
                            ;THROW 51010, 'settings_manufacturing_tolerance_lifecycle_initial_state_invalid', 1;
                        END;
                        RETURN;
                    END;

                    IF EXISTS
                    (
                        SELECT 1
                        FROM deleted AS d
                        LEFT JOIN inserted AS i
                            ON d.[CompanyId] = i.[CompanyId] AND d.[ProfileId] = i.[ProfileId]
                        WHERE i.[ProfileId] IS NULL
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM inserted AS i
                        LEFT JOIN deleted AS d
                            ON d.[CompanyId] = i.[CompanyId] AND d.[ProfileId] = i.[ProfileId]
                        WHERE d.[ProfileId] IS NULL
                    )
                    BEGIN
                        ;THROW 51010, 'settings_manufacturing_tolerance_lifecycle_identity_immutable', 1;
                    END;

                    IF EXISTS
                    (
                        SELECT 1
                        FROM inserted AS i
                        INNER JOIN deleted AS d
                            ON d.[CompanyId] = i.[CompanyId] AND d.[ProfileId] = i.[ProfileId]
                        WHERE d.[Status] = 3
                           OR i.[StateVersion] <> d.[StateVersion] + 1
                           OR NOT
                           (
                               (d.[Status] = 1 AND i.[Status] = 1)
                               OR (d.[Status] = 1 AND i.[Status] = 2)
                               OR (d.[Status] = 2 AND i.[Status] = 3)
                           )
                           OR EXISTS
                           (
                               SELECT d.[CompanyId], d.[ProfileId], d.[Process], d.[ProfileVersion]
                               EXCEPT
                               SELECT i.[CompanyId], i.[ProfileId], i.[Process], i.[ProfileVersion]
                           )
                           OR
                           (
                               d.[Status] = 1 AND i.[Status] = 2
                               AND EXISTS
                               (
                                   SELECT d.[MaximumFirePercent], d.[EffectiveFrom], d.[EffectiveThrough],
                                          d.[PreparedBy], d.[PreparedAtUtc], d.[PreparationReason],
                                          d.[PreparationCorrelationId], d.[PreparationCausationId],
                                          d.[PreparationSourceChannel], d.[PreparationClientIpAddress],
                                          d.[PreparationUserAgent]
                                   EXCEPT
                                   SELECT i.[MaximumFirePercent], i.[EffectiveFrom], i.[EffectiveThrough],
                                          i.[PreparedBy], i.[PreparedAtUtc], i.[PreparationReason],
                                          i.[PreparationCorrelationId], i.[PreparationCausationId],
                                          i.[PreparationSourceChannel], i.[PreparationClientIpAddress],
                                          i.[PreparationUserAgent]
                               )
                           )
                           OR
                           (
                               d.[Status] = 2 AND i.[Status] = 3
                               AND EXISTS
                               (
                                   SELECT d.[MaximumFirePercent], d.[EffectiveFrom], d.[EffectiveThrough],
                                          d.[PreparedBy], d.[PreparedAtUtc], d.[PreparationReason],
                                          d.[PreparationCorrelationId], d.[PreparationCausationId],
                                          d.[PreparationSourceChannel], d.[PreparationClientIpAddress],
                                          d.[PreparationUserAgent], d.[ApprovedBy], d.[ApprovedAtUtc],
                                          d.[ApprovalReason], d.[ApprovalCorrelationId], d.[ApprovalCausationId],
                                          d.[ApprovalSourceChannel], d.[ApprovalClientIpAddress], d.[ApprovalUserAgent]
                                   EXCEPT
                                   SELECT i.[MaximumFirePercent], i.[EffectiveFrom], i.[EffectiveThrough],
                                          i.[PreparedBy], i.[PreparedAtUtc], i.[PreparationReason],
                                          i.[PreparationCorrelationId], i.[PreparationCausationId],
                                          i.[PreparationSourceChannel], i.[PreparationClientIpAddress],
                                          i.[PreparationUserAgent], i.[ApprovedBy], i.[ApprovedAtUtc],
                                          i.[ApprovalReason], i.[ApprovalCorrelationId], i.[ApprovalCausationId],
                                          i.[ApprovalSourceChannel], i.[ApprovalClientIpAddress], i.[ApprovalUserAgent]
                               )
                           )
                    )
                    BEGIN
                        ;THROW 51010, 'settings_manufacturing_tolerance_lifecycle_immutable', 1;
                    END;

                    IF EXISTS
                    (
                        SELECT 1
                        FROM inserted AS i
                        INNER JOIN [settings].[ManufacturingToleranceProfileLifecycles] AS other
                            ON other.[CompanyId] = i.[CompanyId]
                           AND other.[Process] = i.[Process]
                           AND other.[Status] = 3
                           AND other.[ProfileId] <> i.[ProfileId]
                           AND other.[EffectiveFrom] <= COALESCE(i.[EffectiveThrough], CONVERT(date, '99991231', 112))
                           AND (other.[EffectiveThrough] IS NULL OR other.[EffectiveThrough] >= i.[EffectiveFrom])
                        WHERE i.[Status] = 3
                    )
                    BEGIN
                        ;THROW 51011, 'settings_manufacturing_tolerance_publication_overlap', 1;
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [settings].[ManufacturingToleranceProfileLifecycles])
                BEGIN
                    ;THROW 51012, 'settings_manufacturing_tolerance_lifecycle_downgrade_requires_empty_table', 1;
                END;

                DROP TRIGGER IF EXISTS [settings].[TR_ManufacturingToleranceProfileLifecycles_Protect];
                """);

            migrationBuilder.DropTable(
                name: "ManufacturingToleranceProfileLifecycles",
                schema: "settings");
        }
    }
}
