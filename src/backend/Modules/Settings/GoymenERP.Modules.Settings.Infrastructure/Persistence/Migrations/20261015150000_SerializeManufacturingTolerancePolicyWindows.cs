using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;

/// <summary>
/// Serializes approved Manufacturing tolerance publications for each company and process before
/// base-table keys are inserted, then evaluates effective-window overlap under deterministic
/// serializable range locks.
/// </summary>
[DbContext(typeof(SettingsDbContext))]
[Migration(MigrationId)]
public sealed class SerializeManufacturingTolerancePolicyWindows : Migration
{
    public const string MigrationId = "20261015150000_SerializeManufacturingTolerancePolicyWindows";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @ExistingRowCount bigint;
            SELECT @ExistingRowCount = COUNT_BIG(*)
            FROM [settings].[ManufacturingTolerancePolicies] WITH (TABLOCKX, HOLDLOCK);

            IF EXISTS (
                SELECT 1
                FROM [settings].[ManufacturingTolerancePolicies] AS first_existing
                INNER JOIN [settings].[ManufacturingTolerancePolicies] AS second_existing
                    ON second_existing.[CompanyId] = first_existing.[CompanyId]
                    AND second_existing.[Process] = first_existing.[Process]
                    AND second_existing.[ProfileId] <> first_existing.[ProfileId]
                    AND second_existing.[EffectiveFrom] <= ISNULL(first_existing.[EffectiveThrough], CONVERT(date, '99991231'))
                    AND first_existing.[EffectiveFrom] <= ISNULL(second_existing.[EffectiveThrough], CONVERT(date, '99991231')))
            BEGIN
                THROW 51004, 'settings_manufacturing_tolerance_existing_history_overlap', 1;
            END
            """);
        migrationBuilder.Sql(
            """
            CREATE OR ALTER TRIGGER [settings].[TR_ManufacturingTolerancePolicies_NoOverlap]
            ON [settings].[ManufacturingTolerancePolicies]
            INSTEAD OF INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    THROW 51002, 'settings_manufacturing_tolerance_profile_immutable', 1;
                END

                DECLARE @CompanyId uniqueidentifier;
                DECLARE @Process varchar(32);
                DECLARE @LockedRowCount bigint;

                DECLARE affected_keys CURSOR LOCAL STATIC READ_ONLY FOR
                    SELECT DISTINCT incoming.[CompanyId], incoming.[Process]
                    FROM inserted AS incoming
                    ORDER BY incoming.[CompanyId], incoming.[Process];

                OPEN affected_keys;
                FETCH NEXT FROM affected_keys INTO @CompanyId, @Process;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SELECT @LockedRowCount = COUNT_BIG(*)
                    FROM [settings].[ManufacturingTolerancePolicies] WITH
                        (UPDLOCK, HOLDLOCK, INDEX([IX_ManufacturingTolerancePolicies_CompanyId_Process_EffectiveFrom]))
                    WHERE [CompanyId] = @CompanyId
                      AND [Process] = @Process;

                    FETCH NEXT FROM affected_keys INTO @CompanyId, @Process;
                END
                CLOSE affected_keys;
                DEALLOCATE affected_keys;

                IF EXISTS (
                    SELECT 1
                    FROM inserted AS incoming
                    INNER JOIN [settings].[ManufacturingTolerancePolicies] AS existing WITH
                        (UPDLOCK, HOLDLOCK, INDEX([IX_ManufacturingTolerancePolicies_CompanyId_Process_EffectiveFrom]))
                        ON existing.[CompanyId] = incoming.[CompanyId]
                        AND existing.[Process] = incoming.[Process]
                        AND existing.[ProfileId] <> incoming.[ProfileId]
                        AND existing.[EffectiveFrom] <= ISNULL(incoming.[EffectiveThrough], CONVERT(date, '99991231'))
                        AND incoming.[EffectiveFrom] <= ISNULL(existing.[EffectiveThrough], CONVERT(date, '99991231')))
                BEGIN
                    THROW 51001, 'settings_manufacturing_tolerance_window_overlap', 1;
                END

                IF EXISTS (
                    SELECT 1
                    FROM inserted AS first_incoming
                    INNER JOIN inserted AS second_incoming
                        ON second_incoming.[CompanyId] = first_incoming.[CompanyId]
                        AND second_incoming.[Process] = first_incoming.[Process]
                        AND second_incoming.[ProfileId] <> first_incoming.[ProfileId]
                        AND second_incoming.[EffectiveFrom] <= ISNULL(first_incoming.[EffectiveThrough], CONVERT(date, '99991231'))
                        AND first_incoming.[EffectiveFrom] <= ISNULL(second_incoming.[EffectiveThrough], CONVERT(date, '99991231')))
                BEGIN
                    THROW 51001, 'settings_manufacturing_tolerance_window_overlap', 1;
                END

                -- SQL Server does not recursively invoke an INSTEAD OF trigger when that trigger
                -- writes to the same base table. The following statement proceeds directly to
                -- constraint enforcement and preserves the exact incoming rows.
                INSERT INTO [settings].[ManufacturingTolerancePolicies]
                    ([CompanyId], [ProfileId], [Process], [Version], [MaximumFirePercent], [IsActive],
                     [EffectiveFrom], [EffectiveThrough], [ApprovalStatus], [ApprovedBy], [ApprovedAtUtc],
                     [ApprovalReason])
                SELECT [CompanyId], [ProfileId], [Process], [Version], [MaximumFirePercent], [IsActive],
                       [EffectiveFrom], [EffectiveThrough], [ApprovalStatus], [ApprovedBy], [ApprovedAtUtc],
                       [ApprovalReason]
                FROM inserted;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM [settings].[ManufacturingTolerancePolicies])
                THROW 51003, 'settings_manufacturing_tolerance_history_exists', 1;
            """);
        migrationBuilder.Sql(
            """
            CREATE OR ALTER TRIGGER [settings].[TR_ManufacturingTolerancePolicies_NoOverlap]
            ON [settings].[ManufacturingTolerancePolicies]
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                BEGIN
                    THROW 51002, 'settings_manufacturing_tolerance_profile_immutable', 1;
                END
                IF EXISTS (
                    SELECT 1
                    FROM inserted AS incoming
                    INNER JOIN [settings].[ManufacturingTolerancePolicies] AS existing
                        ON existing.[CompanyId] = incoming.[CompanyId]
                        AND existing.[Process] = incoming.[Process]
                        AND existing.[ProfileId] <> incoming.[ProfileId]
                        AND existing.[EffectiveFrom] <= ISNULL(incoming.[EffectiveThrough], CONVERT(date, '99991231'))
                        AND incoming.[EffectiveFrom] <= ISNULL(existing.[EffectiveThrough], CONVERT(date, '99991231')))
                BEGIN
                    THROW 51001, 'settings_manufacturing_tolerance_window_overlap', 1;
                END
            END
            """);
    }
}
