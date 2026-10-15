using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;

/// <summary>
/// Serializes approved Manufacturing tolerance publications for each company and process before
/// evaluating effective-window overlap. The application lock is transaction-owned, so concurrent
/// direct SQL writers and future command handlers share the same database-enforced boundary.
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

                DECLARE @CompanyId uniqueidentifier;
                DECLARE @Process varchar(32);
                DECLARE @LockResource nvarchar(255);
                DECLARE @LockResult int;

                DECLARE affected_keys CURSOR LOCAL STATIC READ_ONLY FOR
                    SELECT DISTINCT incoming.[CompanyId], incoming.[Process]
                    FROM inserted AS incoming
                    ORDER BY incoming.[CompanyId], incoming.[Process];

                OPEN affected_keys;
                FETCH NEXT FROM affected_keys INTO @CompanyId, @Process;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @LockResource = CONCAT(
                        N'settings:manufacturing-tolerance:',
                        CONVERT(nvarchar(36), @CompanyId),
                        N':',
                        @Process);

                    EXEC @LockResult = sys.sp_getapplock
                        @Resource = @LockResource,
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = -1,
                        @DbPrincipal = 'public';

                    IF @LockResult < 0
                    BEGIN
                        CLOSE affected_keys;
                        DEALLOCATE affected_keys;
                        THROW 51004, 'settings_manufacturing_tolerance_serialization_unavailable', 1;
                    END

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
